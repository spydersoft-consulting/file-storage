namespace Spydersoft.FileStore.Contracts;

public interface IFileStoreClient
{
    Task<InitiateUploadResponse> InitiateUploadAsync(InitiateUploadRequest request, CancellationToken cancellationToken = default);
    Task ConfirmUploadAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileDto>> ListFilesAsync(string? source = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);
    Task<FileDto?> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<FileUrlResponse> GetDownloadUrlAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
}
