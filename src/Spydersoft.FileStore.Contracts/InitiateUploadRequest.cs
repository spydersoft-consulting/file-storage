namespace Spydersoft.FileStore.Contracts;

public record InitiateUploadRequest(
    string Source,
    string EntityType,
    string EntityId,
    string FileName,
    string ContentType,
    long SizeBytes);
