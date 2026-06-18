namespace Spydersoft.FileStoreApi.Infrastructure.Storage;

public interface IStorageClient
{
    Task<string> GenerateUploadUrlAsync(string storageKey, string contentType, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<string> GenerateDownloadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<bool> ObjectExistsAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken = default);
}
