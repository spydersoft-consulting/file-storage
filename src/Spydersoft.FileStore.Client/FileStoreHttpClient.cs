using System.Net;
using System.Net.Http.Json;
using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStore.Client;

public sealed class FileStoreHttpClient : IFileStoreClient
{
    private readonly HttpClient _http;

    public FileStoreHttpClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<InitiateUploadResponse> InitiateUploadAsync(InitiateUploadRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/filestore", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InitiateUploadResponse>(cancellationToken))!;
    }

    public async Task ConfirmUploadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"/api/v1/filestore/{fileId}/confirm", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<FileDto>> ListFilesAsync(string? source = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default)
    {
        var query = HttpClientHelpers.BuildQuery([("source", source), ("entityType", entityType), ("entityId", entityId)]);
        var result = await _http.GetFromJsonAsync<List<FileDto>>($"/api/v1/filestore{query}", cancellationToken);
        return result ?? [];
    }

    public async Task<FileDto?> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/api/v1/filestore/{fileId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FileDto>(cancellationToken);
    }

    public async Task<FileUrlResponse> GetFileUrlAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<FileUrlResponse>($"/api/v1/filestore/{fileId}/url", cancellationToken);
        return response!;
    }

    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/filestore/{fileId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
