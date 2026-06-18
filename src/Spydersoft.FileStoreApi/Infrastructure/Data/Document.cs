using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStoreApi.Infrastructure.Data;

public sealed class Document
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RetentionPolicy RetentionPolicy { get; set; }
    public int? RetentionCount { get; set; }
    public bool IsDeleted { get; set; }
    public ICollection<DocumentVersion> Versions { get; set; } = [];
}
