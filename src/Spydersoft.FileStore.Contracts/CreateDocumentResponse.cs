namespace Spydersoft.FileStore.Contracts;

public record CreateDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    Guid FileId,
    string UploadUrl,
    DateTimeOffset UploadExpiresAt);
