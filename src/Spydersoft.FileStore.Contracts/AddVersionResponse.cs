namespace Spydersoft.FileStore.Contracts;

public record AddVersionResponse(
    Guid VersionId,
    Guid FileId,
    string UploadUrl,
    DateTimeOffset UploadExpiresAt);
