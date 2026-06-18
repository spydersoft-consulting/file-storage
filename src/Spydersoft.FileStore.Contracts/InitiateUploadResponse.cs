namespace Spydersoft.FileStore.Contracts;

public record InitiateUploadResponse(
    Guid FileId,
    string UploadUrl,
    DateTimeOffset UploadExpiresAt);
