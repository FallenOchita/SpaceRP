using Microsoft.Extensions.Configuration;
using SpaceRP.Server.Authentication;
using Xunit;

namespace SpaceRP.Server.Tests;

public sealed class DeviceAuthorizationStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "SpaceRP.Tests",
        Guid.NewGuid().ToString("N"),
        "device-auth.db");

    private DeviceAuthorizationStore CreateStore() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SpaceRP:DatabasePath"] = _databasePath
            })
            .Build());

    [Fact]
    public async Task CreateAsync_ReturnsUniqueCodesAndPendingRequest()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var first = await store.CreateAsync();
        var second = await store.CreateAsync();

        Assert.NotEqual(first.DeviceCode, second.DeviceCode);
        Assert.NotEqual(first.UserCode, second.UserCode);
        Assert.Equal(8, first.UserCode.Length);
        Assert.DoesNotContain('-', first.UserCode);
        Assert.True(first.ExpiresAt > DateTimeOffset.UtcNow);

        var poll = await store.PollAsync(first.DeviceCode);
        Assert.Equal(DeviceAuthorizationStatus.Pending, poll.Status);
    }

    [Fact]
    public async Task ApproveAsync_BindsRequestToAuthenticatedAccount_AndPollConsumesOnce()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var request = await store.CreateAsync();
        var accountId = Guid.NewGuid();

        Assert.True(await store.ApproveAsync(request.UserCode, accountId));
        Assert.False(await store.ApproveAsync(request.UserCode, Guid.NewGuid()));

        var approved = await store.PollAsync(request.DeviceCode);
        Assert.Equal(DeviceAuthorizationStatus.Approved, approved.Status);
        Assert.Equal(accountId, approved.AccountId);

        var secondPoll = await store.PollAsync(request.DeviceCode);
        Assert.Equal(DeviceAuthorizationStatus.AlreadyConsumed, secondPoll.Status);
    }

    [Fact]
    public async Task ApproveAsync_AcceptsFormattedUserCode()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var request = await store.CreateAsync();
        var formatted = $"{request.UserCode[..4]}-{request.UserCode[4..]}";

        Assert.True(await store.ApproveAsync(formatted, Guid.NewGuid()));
    }

    [Fact]
    public async Task DeviceAuthorization_PersistsAcrossStoreRecreation()
    {
        var firstStore = CreateStore();
        await firstStore.InitializeAsync();
        var request = await firstStore.CreateAsync();

        var secondStore = CreateStore();
        await secondStore.InitializeAsync();
        var poll = await secondStore.PollAsync(request.DeviceCode);

        Assert.Equal(DeviceAuthorizationStatus.Pending, poll.Status);
    }

    [Fact]
    public async Task PollAsync_RejectsUnknownDeviceCode()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var poll = await store.PollAsync("not-a-device-code");

        Assert.Equal(DeviceAuthorizationStatus.Invalid, poll.Status);
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
