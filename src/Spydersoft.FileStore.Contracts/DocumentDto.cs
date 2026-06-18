namespace Spydersoft.FileStore.Contracts;

public sealed record DocumentDto
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public RetentionPolicy RetentionPolicy { get; init; }
    public int? RetentionCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DocumentVersionDto? CurrentVersion { get; init; }
}
