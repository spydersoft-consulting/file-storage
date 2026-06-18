namespace Spydersoft.FileStore.Contracts;

public sealed record InitiateUploadResponse
{
    public Guid FileId { get; init; }
    public string UploadUrl { get; init; } = string.Empty;
    public DateTimeOffset UploadExpiresAt { get; init; }
}
