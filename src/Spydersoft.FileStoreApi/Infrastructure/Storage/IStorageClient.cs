namespace Spydersoft.FileStoreApi.Infrastructure.Storage;

public interface IStorageClient
{
    Task<string> GenerateUploadUrlAsync(string storageKey, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}
