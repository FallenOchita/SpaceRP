using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceRP.Launcher.Services;

public sealed class DeviceAuthorizationClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(
        Uri serverBaseUri,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            new Uri(serverBaseUri, "/auth/device/code"),
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(
                   JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("The server returned an empty device-code response.");
    }

    public async Task<DeviceTokenResponse?> PollForTokenAsync(
        Uri serverBaseUri,
        string deviceCode,
        int intervalSeconds,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 3, 30));
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(interval, cancellationToken);

            using var response = await httpClient.PostAsJsonAsync(
                new Uri(serverBaseUri, "/auth/device/token"),
                new DeviceTokenRequest(deviceCode),
                JsonOptions,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(
                           JsonOptions, cancellationToken)
                       ?? throw new InvalidOperationException("The server returned an empty session response.");
            }

            var error = await response.Content.ReadFromJsonAsync<DeviceTokenError>(
                JsonOptions, cancellationToken);

            if (error?.Error == "authorization_pending")
                continue;

            if (error?.Error == "slow_down")
            {
                interval = TimeSpan.FromSeconds(Math.Min(interval.TotalSeconds + 5, 30));
                continue;
            }

            if (error?.Error == "expired_token")
                throw new TimeoutException("The device authorization code expired. Start sign-in again.");

            throw new HttpRequestException(
                $"The server rejected device authorization ({error?.Error ?? (int)response.StatusCode}).");
        }

        throw new TimeoutException("The device authorization code expired. Start sign-in again.");
    }
}

public sealed record DeviceCodeResponse(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval);

public sealed record DeviceTokenRequest(
    [property: JsonPropertyName("device_code")] string DeviceCode);

public sealed record DeviceTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("account")] JsonElement Account);

public sealed record DeviceTokenError(
    [property: JsonPropertyName("error")] string? Error);
