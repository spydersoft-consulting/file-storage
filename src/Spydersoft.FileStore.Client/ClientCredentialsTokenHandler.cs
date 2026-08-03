using IdentityModel.Client;
using Microsoft.Extensions.Options;

namespace Spydersoft.FileStore.Client;

/// <summary>
/// Attaches an OAuth2 client-credentials bearer token to outgoing requests when
/// <see cref="FileStoreOptions.TokenEndpoint"/> and <see cref="FileStoreOptions.ClientId"/> are configured.
/// The token is cached in memory and refreshed shortly before it expires. When no token endpoint is
/// configured, requests are passed through unmodified so local/dev usage without auth keeps working.
/// </summary>
public sealed class ClientCredentialsTokenHandler : DelegatingHandler
{
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FileStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ClientCredentialsTokenHandler(IHttpClientFactory httpClientFactory, IOptions<FileStoreOptions> options)
        : this(httpClientFactory, options, TimeProvider.System)
    {
    }

    internal ClientCredentialsTokenHandler(IHttpClientFactory httpClientFactory, IOptions<FileStoreOptions> options, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TokenEndpoint) || string.IsNullOrWhiteSpace(_options.ClientId))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var token = await GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _timeProvider.GetUtcNow() < _expiresAt)
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _timeProvider.GetUtcNow() < _expiresAt)
            {
                return _accessToken;
            }

            var tokenClient = _httpClientFactory.CreateClient(FileStoreServiceCollectionExtensions.TokenClientName);
            var response = await tokenClient.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = _options.TokenEndpoint,
                ClientId = _options.ClientId!,
                ClientSecret = _options.ClientSecret,
                Scope = _options.Scope,
            }, cancellationToken);

            if (response.IsError)
            {
                throw new InvalidOperationException($"Failed to acquire a FileStore access token: {response.Error}", response.Exception);
            }

            _accessToken = response.AccessToken!;
            _expiresAt = _timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn) - ExpiryBuffer;

            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
