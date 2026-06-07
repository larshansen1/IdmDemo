using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Backend.Application.Models.Auth;

namespace Backend.Mcp.Api;

public sealed class IdmApiTokenProvider : IIdmApiTokenProvider
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly Dictionary<string, (BoundToken Token, DateTimeOffset Expiry)> _cache = new(StringComparer.Ordinal);

    private readonly IIdmApiInstanceResolver _instanceResolver;
    private readonly IHttpClientFactory _httpClientFactory;

    public IdmApiTokenProvider(IIdmApiInstanceResolver instanceResolver, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(instanceResolver);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        this._instanceResolver = instanceResolver;
        this._httpClientFactory = httpClientFactory;
    }

    public async Task<BoundToken> GetBoundTokenAsync(string instanceName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(instanceName, out var cached) &&
                cached.Expiry > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return cached.Token;
            }

            return await this.AcquireTokenAsync(instanceName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static X509Certificate2 LoadCertificate(string path)
    {
        try
        {
            return X509Certificate2.CreateFromPemFile(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or CryptographicException or DirectoryNotFoundException)
        {
            throw new McpConfigurationException(
                $"Could not load client certificate from '{path}': {ex.Message}");
        }
    }

    private async Task<BoundToken> AcquireTokenAsync(string instanceName, CancellationToken cancellationToken)
    {
        var resolved = this._instanceResolver.Resolve(instanceName);

        using var cert = LoadCertificate(resolved.ClientCertificatePath);
        using var httpClient = this._httpClientFactory.CreateClient("idm-token");
        var tokenUri = new Uri(resolved.BaseUrl, "connect/token");

#pragma warning disable CA2000 // dpopKey is owned by BoundToken stored in the cache; disposal is managed by cache eviction
        var dpopKey = RSA.Create(2048);
#pragma warning restore CA2000
        var dpopProof = DpopProofFactory.Create(dpopKey, "POST", tokenUri);

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri);
        request.Headers.Add("X-Client-Cert", Convert.ToBase64String(cert.RawData));
        request.Headers.Add("DPoP", dpopProof);
        request.Content = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("grant_type", "client_credentials"),
            KeyValuePair.Create("client_id", resolved.ClientId),
        ]);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Token endpoint returned an empty response.");

        var token = new BoundToken(tokenResponse.AccessToken, dpopKey);
        var expiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        _cache[instanceName] = (token, expiry);
        return token;
    }
}
