namespace Spydersoft.FileStore.Contracts;

public record FileDto(
    Guid Id,
    string TenantId,
    string Source,
    string EntityType,
    string EntityId,
    string FileName,
    string ContentType,
    long SizeBytes,
    FileStatus Status,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ConfirmedAt);
