namespace Spydersoft.FileStore.Contracts;

public record DocumentVersionDto(
    Guid Id,
    Guid DocumentId,
    Guid FileId,
    int VersionNumber,
    string? Comment,
    DocumentVersionStatus Status,
    DateTimeOffset UploadedAt,
    string UploadedBy);
