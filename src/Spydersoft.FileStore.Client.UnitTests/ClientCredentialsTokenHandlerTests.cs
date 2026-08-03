using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Spydersoft.FileStore.Client.UnitTests;

[TestFixture]
public class ClientCredentialsTokenHandlerTests
{
    private static HttpRequestMessage CreateRequest() =>
        new(HttpMethod.Get, "https://filestore.example.com/api/v1/filestore");

    private static MockHttpMessageHandler CreateTokenHandler(string accessToken = "test-token", int expiresIn = 3600) =>
        new(
            HttpStatusCode.OK,
            new StringContent(
                $$"""{"access_token":"{{accessToken}}","expires_in":{{expiresIn}},"token_type":"Bearer"}""",
                Encoding.UTF8,
                "application/json"));

    private static ClientCredentialsTokenHandler CreateHandler(
        FileStoreOptions options,
        MockHttpMessageHandler tokenHandler,
        MockHttpMessageHandler innerHandler,
        TimeProvider? timeProvider = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(FileStoreServiceCollectionExtensions.TokenClientName).Returns(new HttpClient(tokenHandler));

        var handler = new ClientCredentialsTokenHandler(factory, Options.Create(options), timeProvider ?? TimeProvider.System)
        {
            InnerHandler = innerHandler,
        };
        return handler;
    }

    [Test]
    public async Task SendAsync_WithNoTokenEndpointConfigured_PassesThroughWithoutAttachingToken()
    {
        var options = new FileStoreOptions { BaseUrl = "https://filestore.example.com" };
        var tokenHandler = CreateTokenHandler();
        var innerHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var handler = CreateHandler(options, tokenHandler, innerHandler);

        using var invoker = new HttpMessageInvoker(handler);
        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(innerHandler.LastRequest!.Headers.Authorization, Is.Null);
        Assert.That(tokenHandler.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task SendAsync_WithTokenEndpointConfigured_AttachesBearerToken()
    {
        var options = new FileStoreOptions
        {
            BaseUrl = "https://filestore.example.com",
            TokenEndpoint = "https://auth.example.com/connect/token",
            ClientId = "pitstop-api",
            ClientSecret = "secret",
            Scope = "filestore:read filestore:write",
        };
        var tokenHandler = CreateTokenHandler("access-123");
        var innerHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var handler = CreateHandler(options, tokenHandler, innerHandler);

        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.That(innerHandler.LastRequest!.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(innerHandler.LastRequest!.Headers.Authorization!.Parameter, Is.EqualTo("access-123"));
        Assert.That(tokenHandler.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SendAsync_CalledTwiceBeforeExpiry_ReusesCachedToken()
    {
        var options = new FileStoreOptions
        {
            BaseUrl = "https://filestore.example.com",
            TokenEndpoint = "https://auth.example.com/connect/token",
            ClientId = "pitstop-api",
            ClientSecret = "secret",
        };
        var tokenHandler = CreateTokenHandler(expiresIn: 3600);
        var innerHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var handler = CreateHandler(options, tokenHandler, innerHandler);

        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(CreateRequest(), CancellationToken.None);
        await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.That(tokenHandler.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SendAsync_AfterTokenExpires_FetchesANewToken()
    {
        var options = new FileStoreOptions
        {
            BaseUrl = "https://filestore.example.com",
            TokenEndpoint = "https://auth.example.com/connect/token",
            ClientId = "pitstop-api",
            ClientSecret = "secret",
        };
        var tokenHandler = CreateTokenHandler(expiresIn: 120);
        var innerHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var timeProvider = new FakeTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = CreateHandler(options, tokenHandler, innerHandler, timeProvider);

        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        timeProvider.UtcNow += TimeSpan.FromSeconds(61);
        await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.That(tokenHandler.CallCount, Is.EqualTo(2));
    }

    [Test]
    public void SendAsync_WhenTokenRequestFails_ThrowsInvalidOperationException()
    {
        var options = new FileStoreOptions
        {
            BaseUrl = "https://filestore.example.com",
            TokenEndpoint = "https://auth.example.com/connect/token",
            ClientId = "pitstop-api",
            ClientSecret = "wrong-secret",
        };
        var tokenHandler = new MockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            new StringContent("""{"error":"invalid_client"}""", Encoding.UTF8, "application/json"));
        var innerHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var handler = CreateHandler(options, tokenHandler, innerHandler);

        using var invoker = new HttpMessageInvoker(handler);
        Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(CreateRequest(), CancellationToken.None));
    }
}
