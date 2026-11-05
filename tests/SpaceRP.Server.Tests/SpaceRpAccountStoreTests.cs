using Microsoft.Extensions.Configuration;
using SpaceRP.Server.Accounts;
using Xunit;

namespace SpaceRP.Server.Tests;

public sealed class SpaceRpAccountStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "SpaceRP.Tests",
        Guid.NewGuid().ToString("N"),
        "accounts.db");

    private SpaceRpAccountStore CreateStore() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SpaceRP:DatabasePath"] = _databasePath
            })
            .Build());

    [Fact]
    public async Task CreateSession_CreatesPersistentAccountAndSession()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var session = await store.CreateSessionAsync(
            "76561198000000001",
            TimeSpan.FromHours(12));

        var account = await store.FindAccountBySessionAsync(session.Token);

        Assert.NotNull(account);
        Assert.Equal(session.Account.AccountId, account.AccountId);
        Assert.Equal("76561198000000001", account.SteamId64);
        Assert.Equal(12, (session.ExpiresAt - session.Account.LastLoginAt).TotalHours, precision: 0);
    }

    [Fact]
    public async Task CreateSession_ReusesAccountForSameSteamIdentity()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var first = await store.CreateSessionAsync(
            "76561198000000002",
            TimeSpan.FromHours(12));
        var second = await store.CreateSessionAsync(
            "76561198000000002",
            TimeSpan.FromHours(12));

        Assert.Equal(first.Account.AccountId, second.Account.AccountId);
        Assert.NotEqual(first.Token, second.Token);
        Assert.NotNull(await store.FindAccountBySessionAsync(first.Token));
        Assert.NotNull(await store.FindAccountBySessionAsync(second.Token));
    }

    [Fact]
    public async Task RevokeSession_PreventsFurtherAuthentication()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var session = await store.CreateSessionAsync(
            "76561198000000003",
            TimeSpan.FromHours(12));

        await store.RevokeSessionAsync(session.Token);

        Assert.Null(await store.FindAccountBySessionAsync(session.Token));
    }

    [Fact]
    public async Task Session_RemainsValidAfterStoreIsRecreated()
    {
        var firstStore = CreateStore();
        await firstStore.InitializeAsync();
        var session = await firstStore.CreateSessionAsync(
            "76561198000000004",
            TimeSpan.FromHours(12));

        var secondStore = CreateStore();
        await secondStore.InitializeAsync();
        var account = await secondStore.FindAccountBySessionAsync(session.Token);

        Assert.NotNull(account);
        Assert.Equal(session.Account.AccountId, account.AccountId);
    }

    [Fact]
    public async Task CreateSession_RejectsInvalidSteamIdentity()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CreateSessionAsync("not-a-steam-id", TimeSpan.FromHours(12)));
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // SQLite may retain transient files briefly on some CI hosts.
            }
            catch (UnauthorizedAccessException)
            {
                // Test cleanup must not hide the assertion result.
            }
        }
    }
}
