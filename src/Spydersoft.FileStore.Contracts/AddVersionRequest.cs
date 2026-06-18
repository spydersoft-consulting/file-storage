namespace Spydersoft.FileStore.Contracts;

public sealed record AddVersionRequest
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? Comment { get; init; }
}
