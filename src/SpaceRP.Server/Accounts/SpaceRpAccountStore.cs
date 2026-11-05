using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;

namespace SpaceRP.Server.Accounts;

public sealed record AccountProfile(
    Guid AccountId,
    string SteamId64,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastLoginAt);

public sealed record AccountSession(
    AccountProfile Account,
    string Token,
    DateTimeOffset ExpiresAt);

public sealed class SpaceRpAccountStore
{
    private readonly string _connectionString;

    public SpaceRpAccountStore(IConfiguration configuration)
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
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS accounts (
                account_id TEXT NOT NULL PRIMARY KEY,
                steam_id64 TEXT NOT NULL UNIQUE,
                created_at INTEGER NOT NULL,
                last_login_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                token_hash BLOB NOT NULL UNIQUE,
                created_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL,
                revoked_at INTEGER NULL,
                FOREIGN KEY (account_id) REFERENCES accounts(account_id)
            );
            CREATE INDEX IF NOT EXISTS ix_sessions_account_id ON sessions(account_id);
            CREATE INDEX IF NOT EXISTS ix_sessions_expires_at ON sessions(expires_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AccountSession> CreateSessionAsync(
        string steamId64,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (steamId64.Length != 17 || steamId64.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("A verified 17-digit SteamID64 is required.", nameof(steamId64));
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Session lifetime must be between zero and 24 hours.");

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(lifetime);
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var accountId = Guid.NewGuid();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO accounts (account_id, steam_id64, created_at, last_login_at)
                VALUES ($accountId, $steamId64, $now, $now)
                ON CONFLICT(steam_id64) DO UPDATE SET last_login_at = excluded.last_login_at;
                """;
            upsert.Parameters.AddWithValue("$accountId", accountId.ToString("D"));
            upsert.Parameters.AddWithValue("$steamId64", steamId64);
            upsert.Parameters.AddWithValue("$now", now.ToUnixTimeSeconds());
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        AccountProfile account;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT account_id, steam_id64, created_at, last_login_at
                FROM accounts
                WHERE steam_id64 = $steamId64;
                """;
            select.Parameters.AddWithValue("$steamId64", steamId64);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The account could not be loaded after creation.");

            account = new AccountProfile(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)));
        }

        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = transaction;
            cleanup.CommandText = """
                DELETE FROM sessions
                WHERE expires_at < $cutoff
                   OR (revoked_at IS NOT NULL AND revoked_at < $cutoff);
                """;
            cleanup.Parameters.AddWithValue("$cutoff", now.AddDays(-30).ToUnixTimeSeconds());
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertSession = connection.CreateCommand())
        {
            insertSession.Transaction = transaction;
            insertSession.CommandText = """
                INSERT INTO sessions (session_id, account_id, token_hash, created_at, expires_at)
                VALUES ($sessionId, $accountId, $tokenHash, $createdAt, $expiresAt);
                """;
            insertSession.Parameters.AddWithValue("$sessionId", Guid.NewGuid().ToString("D"));
            insertSession.Parameters.AddWithValue("$accountId", account.AccountId.ToString("D"));
            insertSession.Parameters.Add("$tokenHash", SqliteType.Blob).Value = tokenHash;
            insertSession.Parameters.AddWithValue("$createdAt", now.ToUnixTimeSeconds());
            insertSession.Parameters.AddWithValue("$expiresAt", expiresAt.ToUnixTimeSeconds());
            await insertSession.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return new AccountSession(account, token, expiresAt);
    }

    public async Task<AccountProfile?> FindAccountBySessionAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return null;

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.account_id, a.steam_id64, a.created_at, a.last_login_at
            FROM sessions AS s
            INNER JOIN accounts AS a ON a.account_id = s.account_id
            WHERE s.token_hash = $tokenHash
              AND s.revoked_at IS NULL
              AND s.expires_at > $now
            LIMIT 1;
            """;
        command.Parameters.Add("$tokenHash", SqliteType.Blob).Value = tokenHash;
        command.Parameters.AddWithValue("$now", now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AccountProfile(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)))
            : null;
    }

    public async Task RevokeSessionAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return;

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sessions
            SET revoked_at = $now
            WHERE token_hash = $tokenHash AND revoked_at IS NULL;
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.Add("$tokenHash", SqliteType.Blob).Value = tokenHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AccountProfile?> FindAccountByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            return null;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, steam_id64, created_at, last_login_at
            FROM accounts
            WHERE account_id = $accountId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$accountId", accountId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AccountProfile(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)))
            : null;
    }
}
