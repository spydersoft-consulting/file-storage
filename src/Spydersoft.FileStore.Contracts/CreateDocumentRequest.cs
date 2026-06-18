namespace Spydersoft.FileStore.Contracts;

public record CreateDocumentRequest(
    string Source,
    string EntityType,
    string EntityId,
    string Name,
    RetentionPolicy RetentionPolicy,
    int? RetentionCount,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Comment);
