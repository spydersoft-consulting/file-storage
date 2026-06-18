namespace Spydersoft.FileStore.Contracts;

public sealed record CreateDocumentRequest
{
    public string Source { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public RetentionPolicy RetentionPolicy { get; init; } = RetentionPolicy.KeepAll;
    public int? RetentionCount { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? Comment { get; init; }
}
