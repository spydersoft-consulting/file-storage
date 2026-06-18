namespace Spydersoft.FileStoreApi.Services;

// Stub service that logs audit events locally. Wire up Spydersoft.Messaging
// IMessagePublisher in a follow-up when RabbitMQ routing is finalised.
public sealed class AuditEventService
{
    private readonly ILogger<AuditEventService> _logger;

    public AuditEventService(ILogger<AuditEventService> logger)
    {
        _logger = logger;
    }

    public Task EmitDocumentCreatedAsync(Guid documentId, string tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Audit: document.created {DocumentId}", documentId);
        return Task.CompletedTask;
    }

    public Task EmitVersionConfirmedAsync(Guid documentId, Guid versionId, string tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Audit: document.version.confirmed {DocumentId}/{VersionId}", documentId, versionId);
        return Task.CompletedTask;
    }

    public Task EmitVersionDeletedAsync(Guid documentId, Guid versionId, string tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Audit: document.version.deleted {DocumentId}/{VersionId}", documentId, versionId);
        return Task.CompletedTask;
    }

    public Task EmitDocumentDeletedAsync(Guid documentId, string tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Audit: document.deleted {DocumentId}", documentId);
        return Task.CompletedTask;
    }
}
