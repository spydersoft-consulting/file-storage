namespace Spydersoft.FileStore.Contracts;

public interface IDocumentClient
{
    Task<CreateDocumentResponse> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken cancellationToken = default);
    Task<AddVersionResponse> AddVersionAsync(Guid documentId, AddVersionRequest request, CancellationToken cancellationToken = default);
    Task ConfirmVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentDto>> ListDocumentsAsync(string? source = null, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);
    Task<DocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<FileUrlResponse> GetDocumentUrlAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<DocumentVersionDto?> GetVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task<FileUrlResponse> GetVersionUrlAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task DeleteVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
}
