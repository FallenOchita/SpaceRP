using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace SpaceRP.Server.Authentication;

public sealed class SteamOpenIdOptions
{
    public string Realm { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
}

public sealed partial class SteamOpenIdService(IHttpClientFactory httpClientFactory, SteamOpenIdOptions options)
{
    private const string SteamEndpoint = "https://steamcommunity.com/openid/login";
    private const string OpenIdNamespace = "http://specs.openid.net/auth/2.0";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingStates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new(StringComparer.Ordinal);

    public Uri CreateLoginUri()
    {
        var realm = RequireHttpsUri(options.Realm, nameof(options.Realm));
        var callback = RequireHttpsUri(options.CallbackUrl, nameof(options.CallbackUrl));
        if (!string.Equals(callback.GetLeftPart(UriPartial.Authority), realm.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Steam realm and callback must use the same HTTPS origin.");

        PruneExpiredEntries();
        var state = WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        _pendingStates[state] = DateTimeOffset.UtcNow;

        var returnTo = QueryHelpers.AddQueryString(callback.ToString(), "state", state);
        var values = new Dictionary<string, string>
        {
            ["openid.ns"] = OpenIdNamespace,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo,
            ["openid.realm"] = realm.GetLeftPart(UriPartial.Authority),
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };

        return new Uri(QueryHelpers.AddQueryString(SteamEndpoint, values));
    }

    public async Task<string?> ValidateCallbackAsync(IQueryCollection query, CancellationToken cancellationToken)
    {
        var state = query["state"].ToString();
        if (string.IsNullOrWhiteSpace(state) ||
            !_pendingStates.TryRemove(state, out var issuedAt) ||
            DateTimeOffset.UtcNow - issuedAt > StateLifetime)
        {
            return null;
        }

        var callback = RequireHttpsUri(options.CallbackUrl, nameof(options.CallbackUrl));
        var expectedReturnTo = QueryHelpers.AddQueryString(callback.ToString(), "state", state);
        var returnedTo = query["openid.return_to"].ToString();
        if (!string.Equals(returnedTo, expectedReturnTo, StringComparison.Ordinal))
            return null;

        if (query["openid.ns"] != OpenIdNamespace || query["openid.mode"] != "id_res" ||
            query["openid.op_endpoint"] != SteamEndpoint)
            return null;

        var claimedId = query["openid.claimed_id"].ToString();
        var identity = query["openid.identity"].ToString();
        var match = SteamIdPattern().Match(claimedId);
        if (!match.Success || !string.Equals(identity, claimedId, StringComparison.Ordinal))
            return null;

        var nonce = query["openid.response_nonce"].ToString();
        if (string.IsNullOrWhiteSpace(nonce) || !_usedNonces.TryAdd(nonce, DateTimeOffset.UtcNow))
            return null;

        var nonceTime = ParseNonceTimestamp(nonce);
        if (nonceTime is null || DateTimeOffset.UtcNow - nonceTime.Value > TimeSpan.FromMinutes(10) ||
            nonceTime.Value > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            _usedNonces.TryRemove(nonce, out _);
            return null;
        }

        var signedFields = query
            .Where(pair => pair.Key.StartsWith("openid.", StringComparison.OrdinalIgnoreCase))
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.ToString()))
            .Where(pair => pair.Key != "openid.mode")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        signedFields["openid.mode"] = "check_authentication";

        using var response = await httpClientFactory.CreateClient(nameof(SteamOpenIdService))
            .PostAsync(SteamEndpoint, new FormUrlEncodedContent(signedFields), cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(line => string.Equals(line.Trim(), "is_valid:true", StringComparison.OrdinalIgnoreCase)))
            return null;

        return match.Groups["steamId"].Value;
    }

    private void PruneExpiredEntries()
    {
        var cutoff = DateTimeOffset.UtcNow - StateLifetime;
        foreach (var entry in _pendingStates)
        {
            if (entry.Value < cutoff)
                _pendingStates.TryRemove(entry.Key, out _);
        }

        var nonceCutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(12);
        foreach (var entry in _usedNonces)
        {
            if (entry.Value < nonceCutoff)
                _usedNonces.TryRemove(entry.Key, out _);
        }
    }

    private static Uri RequireHttpsUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"Authentication setting '{settingName}' must be an absolute HTTPS URL.");
        }

        return uri;
    }

    private static DateTimeOffset? ParseNonceTimestamp(string nonce)
    {
        var separator = nonce.IndexOf('Z');
        if (separator < 0)
            return null;

        return DateTimeOffset.TryParseExact(
            nonce[..(separator + 1)],
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : null;
    }

    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/(?<steamId>[0-9]{17})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex SteamIdPattern();
}
