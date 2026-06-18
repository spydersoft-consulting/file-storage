using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStoreApi.Infrastructure.Data;

public sealed class DocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DocumentVersionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public bool IsDeleted { get; set; }
    public Document? Document { get; set; }
}
