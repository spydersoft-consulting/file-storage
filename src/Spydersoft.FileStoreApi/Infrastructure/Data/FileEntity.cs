using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStoreApi.Infrastructure.Data;

public sealed class FileEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public FileStatus Status { get; set; }
    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset UploadExpiresAt { get; set; }
    public bool IsDeleted { get; set; }
}
