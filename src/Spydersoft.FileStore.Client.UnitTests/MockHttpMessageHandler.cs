using System.Net;

namespace Spydersoft.FileStore.Client.UnitTests;

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly byte[]? _contentBytes;
    private readonly string? _contentType;

    public HttpRequestMessage? LastRequest { get; private set; }

    public MockHttpMessageHandler(HttpStatusCode statusCode, HttpContent? content = null)
    {
        _statusCode = statusCode;
        if (content is not null)
        {
            _contentBytes = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            _contentType = content.Headers.ContentType?.ToString();
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_statusCode);
        if (_contentBytes is not null)
        {
            response.Content = new ByteArrayContent(_contentBytes);
            if (_contentType is not null)
                response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(_contentType);
        }
        return Task.FromResult(response);
    }
}
