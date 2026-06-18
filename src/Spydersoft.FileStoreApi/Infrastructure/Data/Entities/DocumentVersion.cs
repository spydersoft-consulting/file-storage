using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStoreApi.Infrastructure.Data;

public sealed class DocumentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid FileId { get; set; }
    public int VersionNumber { get; set; }
    public string? Comment { get; set; }
    public DocumentVersionStatus Status { get; set; } = DocumentVersionStatus.Pending;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UploadedBy { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }

    public Document Document { get; set; } = null!;
    public FileEntity File { get; set; } = null!;
}
