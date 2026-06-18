namespace Spydersoft.FileStore.Contracts;

public record DocumentDto(
    Guid Id,
    string TenantId,
    string Source,
    string EntityType,
    string EntityId,
    string Name,
    RetentionPolicy RetentionPolicy,
    int? RetentionCount,
    DateTimeOffset CreatedAt,
    DocumentVersionDto? CurrentVersion);
