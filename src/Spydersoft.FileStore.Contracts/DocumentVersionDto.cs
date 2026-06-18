namespace Spydersoft.FileStore.Contracts;

public sealed record DocumentVersionDto
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid FileId { get; init; }
    public int VersionNumber { get; init; }
    public string? Comment { get; init; }
    public DocumentVersionStatus Status { get; init; }
    public DateTimeOffset UploadedAt { get; init; }
    public string UploadedBy { get; init; } = string.Empty;
}
