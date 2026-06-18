namespace Spydersoft.FileStore.Contracts;

public record AddVersionRequest(
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Comment);
