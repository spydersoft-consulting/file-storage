namespace Spydersoft.FileStore.Contracts;

public sealed record AddVersionResponse
{
    public Guid VersionId { get; init; }
    public Guid FileId { get; init; }
    public string UploadUrl { get; init; } = string.Empty;
    public DateTimeOffset UploadExpiresAt { get; init; }
}
