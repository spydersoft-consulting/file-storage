using System.Net;
using System.Net.Http.Json;
using Spydersoft.FileStore.Client;
using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStore.Client.UnitTests.FileStoreHttpClientTests;

[TestFixture]
public sealed class InitiateUploadTests
{
    [Test]
    public async Task InitiateUpload_ReturnsResponse_WhenSuccessful()
    {
        var expectedResponse = new InitiateUploadResponse(
            Guid.NewGuid(),
            "https://storage.example.com/presigned",
            DateTimeOffset.UtcNow.AddMinutes(15));

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonContent.Create(expectedResponse));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new FileStoreHttpClient(httpClient);

        var request = new InitiateUploadRequest(
            "pitstop",
            "maintenancelog",
            "123",
            "invoice.pdf",
            "application/pdf",
            1024);

        var result = await client.InitiateUploadAsync(request);

        Assert.That(result.FileId, Is.EqualTo(expectedResponse.FileId));
        Assert.That(result.UploadUrl, Is.EqualTo(expectedResponse.UploadUrl));
    }

    [Test]
    public async Task ConfirmUpload_DoesNotThrow_WhenSuccessful()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new FileStoreHttpClient(httpClient);

        Assert.DoesNotThrowAsync(() => client.ConfirmUploadAsync(Guid.NewGuid()));
    }
}
