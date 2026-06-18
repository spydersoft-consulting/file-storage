using System.Net;
using System.Net.Http.Json;
using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStore.Client;

public sealed class DocumentHttpClient : IDocumentClient
{
    private readonly HttpClient _http;

    public DocumentHttpClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<CreateDocumentResponse> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/documents", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateDocumentResponse>(cancellationToken))!;
    }

    public async Task<AddVersionResponse> AddVersionAsync(Guid documentId, AddVersionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync($"/api/v1/documents/{documentId}/versions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddVersionResponse>(cancellationToken))!;
    }

    public async Task ConfirmVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"/api/v1/documents/{documentId}/versions/{versionId}/confirm", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DocumentDto>> ListDocumentsAsync(string? source = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default)
    {
        var query = HttpClientHelpers.BuildQuery([("source", source), ("entityType", entityType), ("entityId", entityId)]);
        var result = await _http.GetFromJsonAsync<List<DocumentDto>>($"/api/v1/documents{query}", cancellationToken);
        return result ?? [];
    }

    public async Task<DocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/api/v1/documents/{documentId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DocumentDto>(cancellationToken);
    }

    public async Task<FileUrlResponse> GetDocumentUrlAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<FileUrlResponse>($"/api/v1/documents/{documentId}/url", cancellationToken);
        return response!;
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var result = await _http.GetFromJsonAsync<List<DocumentVersionDto>>($"/api/v1/documents/{documentId}/versions", cancellationToken);
        return result ?? [];
    }

    public async Task<DocumentVersionDto?> GetVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DocumentVersionDto>(cancellationToken);
    }

    public async Task<FileUrlResponse> GetVersionUrlAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<FileUrlResponse>($"/api/v1/documents/{documentId}/versions/{versionId}/url", cancellationToken);
        return response!;
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/documents/{documentId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/documents/{documentId}/versions/{versionId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
