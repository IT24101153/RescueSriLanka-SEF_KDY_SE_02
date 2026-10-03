using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RescueSriLanka.Api.Services.Push;

/// <summary>The OAuth bearer token that Firebase's HTTP v1 API requires.</summary>
public interface IFcmAccessTokens
{
    Task<string> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Signs a JWT with the service account's private key and exchanges it at
/// Google's token endpoint. The access token is reused until shortly before it
/// expires, so a burst of pushes costs one exchange, not one per message.
/// </summary>
public sealed class FcmAccessTokens(
    IHttpClientFactory httpClientFactory,
    IOptions<PushOptions> options) : IFcmAccessTokens, IDisposable
{
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Credentials? _credentials;
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(CancellationToken ct = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt) return _accessToken;

        await _gate.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt) return _accessToken;

            _credentials ??= Credentials.Load(options.Value.Fcm.CredentialsFile!);

            var now = DateTimeOffset.UtcNow;
            var assertion = BuildAssertion(_credentials, now);

            var client = httpClientFactory.CreateClient(nameof(FcmPushSender));
            using var response = await client.PostAsync(_credentials.TokenUri, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion
                }), ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                // The body can echo request details, so it stays out of the message.
                throw new InvalidOperationException(
                    $"Google refused the Firebase service account ({(int)response.StatusCode}).");
            }

            var grant = JsonSerializer.Deserialize<TokenGrant>(body)
                ?? throw new InvalidOperationException("Google's token response could not be read.");

            _accessToken = grant.AccessToken;
            // Renew a minute early, so a token never expires between being read and being used.
            _expiresAt = now.AddSeconds(grant.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The RS256-signed JWT Google exchanges for an access token. Public so a
    /// test can check its signature against the key's public half.
    /// </summary>
    public static string BuildAssertion(Credentials credentials, DateTimeOffset now)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = credentials.ClientEmail,
            scope = Scope,
            aud = credentials.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddHours(1).ToUnixTimeSeconds()
        }));

        var signingInput = $"{header}.{claims}";
        var signature = credentials.Key.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        _credentials?.Key.Dispose();
        _gate.Dispose();
    }

    /// <summary>The fields of a Firebase service-account file that signing needs.</summary>
    public sealed class Credentials
    {
        public required string ClientEmail { get; init; }
        public required string TokenUri { get; init; }
        public required RSA Key { get; init; }

        public static Credentials Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var key = RSA.Create();
            key.ImportFromPem(root.GetProperty("private_key").GetString()
                ?? throw new InvalidOperationException("The service account has no private key."));

            return new Credentials
            {
                ClientEmail = root.GetProperty("client_email").GetString()
                    ?? throw new InvalidOperationException("The service account has no client_email."),
                TokenUri = root.GetProperty("token_uri").GetString()
                    ?? "https://oauth2.googleapis.com/token",
                Key = key
            };
        }
    }

    private sealed record TokenGrant(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
