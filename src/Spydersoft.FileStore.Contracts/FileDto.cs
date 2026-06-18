namespace Spydersoft.FileStore.Contracts;

public sealed record FileDto
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public FileStatus Status { get; init; }
    public DateTimeOffset InitiatedAt { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
}
