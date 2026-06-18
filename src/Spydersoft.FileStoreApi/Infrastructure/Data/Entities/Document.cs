using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStoreApi.Infrastructure.Data;

public sealed class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? CurrentVersionId { get; set; }
    public RetentionPolicy RetentionPolicy { get; set; } = RetentionPolicy.KeepAll;
    public int? RetentionCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }

    public DocumentVersion? CurrentVersion { get; set; }
    public ICollection<DocumentVersion> Versions { get; set; } = [];
}
