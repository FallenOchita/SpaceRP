using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;

namespace SpaceRP.Server.Authentication;

public sealed record DeviceAuthorizationRequest(
    string DeviceCode,
    string UserCode,
    DateTimeOffset ExpiresAt);

public enum DeviceAuthorizationStatus
{
    Pending,
    Approved,
    Expired,
    Invalid,
    AlreadyConsumed
}

public sealed record DeviceAuthorizationPollResult(
    DeviceAuthorizationStatus Status,
    Guid? AccountId = null);

public sealed class DeviceAuthorizationStore
{
    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly string _connectionString;

    public DeviceAuthorizationStore(IConfiguration configuration)
    {
        var configuredPath = configuration["SpaceRP:DatabasePath"];
        var databasePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "data", "spacerp.db")
            : configuredPath;

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS device_authorizations (
                device_code_hash BLOB NOT NULL PRIMARY KEY,
                user_code TEXT NOT NULL UNIQUE,
                created_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL,
                account_id TEXT NULL,
                approved_at INTEGER NULL,
                consumed_at INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_device_authorizations_expiry
                ON device_authorizations(expires_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DeviceAuthorizationRequest> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var deviceCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var userCode = CreateUserCode();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(10);
        var hash = Hash(deviceCode);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO device_authorizations
                (device_code_hash, user_code, created_at, expires_at)
            VALUES ($hash, $userCode, $createdAt, $expiresAt);
            """;
        command.Parameters.Add("$hash", SqliteType.Blob).Value = hash;
        command.Parameters.AddWithValue("$userCode", userCode);
        command.Parameters.AddWithValue("$createdAt", now.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$expiresAt", expiresAt.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new DeviceAuthorizationRequest(deviceCode, userCode, expiresAt);
    }

    public async Task<bool> ApproveAsync(
        string userCode,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeUserCode(userCode);
        if (normalizedCode.Length != 8 || accountId == Guid.Empty)
            return false;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE device_authorizations
            SET account_id = $accountId, approved_at = $now
            WHERE user_code = $userCode
              AND expires_at > $now
              AND approved_at IS NULL
              AND consumed_at IS NULL;
            """;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        command.Parameters.AddWithValue("$accountId", accountId.ToString("D"));
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$userCode", normalizedCode);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<DeviceAuthorizationPollResult> PollAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode) || deviceCode.Length > 128)
            return new DeviceAuthorizationPollResult(DeviceAuthorizationStatus.Invalid);

        var hash = Hash(deviceCode);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        string? accountId = null;
        long expiresAt;
        long? consumedAt;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT account_id, expires_at, consumed_at
                FROM device_authorizations
                WHERE device_code_hash = $hash;
                """;
            select.Parameters.Add("$hash", SqliteType.Blob).Value = hash;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new DeviceAuthorizationPollResult(DeviceAuthorizationStatus.Invalid);

            accountId = reader.IsDBNull(0) ? null : reader.GetString(0);
            expiresAt = reader.GetInt64(1);
            consumedAt = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        }

        if (expiresAt <= now)
        {
            transaction.Commit();
            return new DeviceAuthorizationPollResult(DeviceAuthorizationStatus.Expired);
        }

        if (consumedAt is not null)
        {
            transaction.Commit();
            return new DeviceAuthorizationPollResult(DeviceAuthorizationStatus.AlreadyConsumed);
        }

        if (accountId is null)
        {
            transaction.Commit();
            return new DeviceAuthorizationPollResult(DeviceAuthorizationStatus.Pending);
        }

        await using (var consume = connection.CreateCommand())
        {
            consume.Transaction = transaction;
            consume.CommandText = """
                UPDATE device_authorizations
                SET consumed_at = $now
                WHERE device_code_hash = $hash
                  AND consumed_at IS NULL
                  AND account_id IS NOT NULL
                  AND expires_at > $now;
                """;
            consume.Parameters.AddWithValue("$now", now);
            consume.Parameters.Add("$hash", SqliteType.Blob).Value = hash;
            if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                transaction.Commit();
                return new DeviceAuthorizationPollResult(DeviceAuthorizationStatus.AlreadyConsumed);
            }
        }

        transaction.Commit();
        return new DeviceAuthorizationPollResult(
            DeviceAuthorizationStatus.Approved,
            Guid.Parse(accountId));
    }

    private static string CreateUserCode()
    {
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);
        Span<char> code = stackalloc char[8];
        for (var index = 0; index < code.Length; index++)
            code[index] = UserCodeAlphabet[random[index] % UserCodeAlphabet.Length];
        return new string(code);
    }

    private static string NormalizeUserCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static byte[] Hash(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
