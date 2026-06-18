using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;
using Spydersoft.FileStoreApi.Services;

namespace Spydersoft.FileStoreApi.Controllers;

[ApiController]
[Route("api/v1/documents")]
[Tags("Documents")]
public class DocumentController(
    FileStoreDbContext db,
    IStorageClient storage,
    AuditEventService audit,
    RetentionPolicyService retention) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(typeof(CreateDocumentResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDocument(CreateDocumentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var file = new FileEntity
        {
            TenantId = tenantId,
            Source = request.Source,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            UploadExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        };
        file.StorageKey = BuildStorageKey(tenantId, request.Source, request.EntityType, request.EntityId, file.Id, request.FileName);

        // Generate the upload URL before committing to DB so that a storage failure
        // does not leave orphaned rows with no upload URL ever delivered.
        var uploadUrl = await storage.GenerateUploadUrlAsync(file.StorageKey, file.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        var document = new Document
        {
            TenantId = tenantId,
            Source = request.Source,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Name = request.Name,
            RetentionPolicy = request.RetentionPolicy,
            RetentionCount = request.RetentionCount,
        };

        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            FileId = file.Id,
            VersionNumber = 1,
            Comment = request.Comment,
            UploadedBy = tenantId,
        };

        db.Files.Add(file);
        db.Documents.Add(document);
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);

        await audit.EmitDocumentCreatedAsync(document.Id, tenantId, cancellationToken);

        return Created($"/api/v1/documents/{document.Id}", new CreateDocumentResponse(document.Id, version.Id, file.Id, uploadUrl, file.UploadExpiresAt));
    }

    [HttpPost("{id:guid}/versions")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(typeof(AddVersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddVersion(Guid id, AddVersionRequest request, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);
        if (document is null)
            return NotFound();

        var maxVersion = await db.DocumentVersions
            .Where(v => v.DocumentId == id && !v.IsDeleted)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0;

        var file = new FileEntity
        {
            TenantId = tenantId,
            Source = document.Source,
            EntityType = document.EntityType,
            EntityId = document.EntityId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            UploadExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        };
        file.StorageKey = BuildStorageKey(tenantId, document.Source, document.EntityType, document.EntityId, file.Id, request.FileName);

        // Generate the upload URL before committing to DB so that a storage failure
        // does not leave orphaned rows with no upload URL ever delivered.
        var uploadUrl = await storage.GenerateUploadUrlAsync(file.StorageKey, file.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            FileId = file.Id,
            VersionNumber = maxVersion + 1,
            Comment = request.Comment,
            UploadedBy = tenantId,
        };

        db.Files.Add(file);
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"/api/v1/documents/{document.Id}/versions/{version.Id}", new AddVersionResponse(version.Id, file.Id, uploadUrl, file.UploadExpiresAt));
    }

    [HttpPost("{id:guid}/versions/{vId:guid}/confirm")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmVersion(Guid id, Guid vId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);
        if (document is null)
            return NotFound();

        var version = await db.DocumentVersions
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted, cancellationToken);

        if (version is null)
            return NotFound();

        var exists = await storage.ObjectExistsAsync(version.File.StorageKey, cancellationToken);
        if (!exists)
            return Problem(detail: "File has not been uploaded to storage yet.", statusCode: StatusCodes.Status422UnprocessableEntity);

        version.Status = DocumentVersionStatus.Confirmed;
        version.File.Status = FileStatus.Confirmed;
        version.File.ConfirmedAt = DateTimeOffset.UtcNow;

        document.CurrentVersionId = version.Id;

        // Save first so retention policy sees the new version as confirmed in the DB.
        await db.SaveChangesAsync(cancellationToken);
        await retention.ApplyAsync(document, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await audit.EmitVersionConfirmedAsync(document.Id, version.Id, tenantId, cancellationToken);

        return NoContent();
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments(
        [FromQuery] string? source,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var query = db.Documents.Where(d => d.TenantId == tenantId && !d.IsDeleted);

        if (!string.IsNullOrEmpty(source))
            query = query.Where(d => d.Source == source);
        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(d => d.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId))
            query = query.Where(d => d.EntityId == entityId);

        var documents = await query
            .Select(d => new DocumentDto(
                d.Id, d.TenantId, d.Source, d.EntityType, d.EntityId,
                d.Name, d.RetentionPolicy, d.RetentionCount, d.CreatedAt,
                d.CurrentVersion == null ? null : new DocumentVersionDto(
                    d.CurrentVersion.Id, d.CurrentVersion.DocumentId, d.CurrentVersion.FileId,
                    d.CurrentVersion.VersionNumber, d.CurrentVersion.Comment,
                    d.CurrentVersion.Status, d.CurrentVersion.UploadedAt, d.CurrentVersion.UploadedBy)))
            .ToListAsync(cancellationToken);

        return Ok(documents);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var document = await db.Documents
            .Where(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted)
            .Select(d => new DocumentDto(
                d.Id, d.TenantId, d.Source, d.EntityType, d.EntityId,
                d.Name, d.RetentionPolicy, d.RetentionCount, d.CreatedAt,
                d.CurrentVersion == null ? null : new DocumentVersionDto(
                    d.CurrentVersion.Id, d.CurrentVersion.DocumentId, d.CurrentVersion.FileId,
                    d.CurrentVersion.VersionNumber, d.CurrentVersion.Comment,
                    d.CurrentVersion.Status, d.CurrentVersion.UploadedAt, d.CurrentVersion.UploadedBy)))
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? NotFound() : Ok(document);
    }

    [HttpGet("{id:guid}/url")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(FileUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentUrl(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var document = await db.Documents
            .Include(d => d.CurrentVersion)
                .ThenInclude(v => v!.File)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (document is null || document.CurrentVersion is null)
            return NotFound();

        var ttl = TimeSpan.FromHours(1);
        var url = await storage.GenerateDownloadUrlAsync(document.CurrentVersion.File.StorageKey, ttl, cancellationToken);
        return Ok(new FileUrlResponse(url, DateTimeOffset.UtcNow.Add(ttl)));
    }

    [HttpGet("{id:guid}/versions")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var exists = await db.Documents.AnyAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);
        if (!exists)
            return NotFound();

        var versions = await db.DocumentVersions
            .Where(v => v.DocumentId == id && !v.IsDeleted)
            .OrderBy(v => v.VersionNumber)
            .Select(v => new DocumentVersionDto(v.Id, v.DocumentId, v.FileId, v.VersionNumber, v.Comment, v.Status, v.UploadedAt, v.UploadedBy))
            .ToListAsync(cancellationToken);

        return Ok(versions);
    }

    [HttpGet("{id:guid}/versions/{vId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(DocumentVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(Guid id, Guid vId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var exists = await db.Documents.AnyAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);
        if (!exists)
            return NotFound();

        var version = await db.DocumentVersions
            .Where(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted)
            .Select(v => new DocumentVersionDto(v.Id, v.DocumentId, v.FileId, v.VersionNumber, v.Comment, v.Status, v.UploadedAt, v.UploadedBy))
            .FirstOrDefaultAsync(cancellationToken);

        return version is null ? NotFound() : Ok(version);
    }

    [HttpGet("{id:guid}/versions/{vId:guid}/url")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(FileUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersionUrl(Guid id, Guid vId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var exists = await db.Documents.AnyAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);
        if (!exists)
            return NotFound();

        var version = await db.DocumentVersions
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted, cancellationToken);

        if (version is null)
            return NotFound();

        var ttl = TimeSpan.FromHours(1);
        var url = await storage.GenerateDownloadUrlAsync(version.File.StorageKey, ttl, cancellationToken);
        return Ok(new FileUrlResponse(url, DateTimeOffset.UtcNow.Add(ttl)));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var document = await db.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (document is null)
            return NotFound();

        document.IsDeleted = true;
        foreach (var version in document.Versions.Where(v => !v.IsDeleted))
            version.IsDeleted = true;

        await db.SaveChangesAsync(cancellationToken);
        await audit.EmitDocumentDeletedAsync(document.Id, tenantId, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}/versions/{vId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVersion(Guid id, Guid vId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);
        if (document is null)
            return NotFound();

        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted, cancellationToken);
        if (version is null)
            return NotFound();

        version.IsDeleted = true;

        // If this was the current version, point to the previous confirmed version
        if (document.CurrentVersionId == version.Id)
        {
            var previousConfirmed = await db.DocumentVersions
                .Where(v => v.DocumentId == id && v.Id != vId && !v.IsDeleted && v.Status == DocumentVersionStatus.Confirmed)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);

            document.CurrentVersionId = previousConfirmed?.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.EmitVersionDeletedAsync(document.Id, version.Id, tenantId, cancellationToken);

        return NoContent();
    }

    private string GetTenantId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? string.Empty;

    private static string BuildStorageKey(string tenantId, string source, string entityType, string entityId, Guid fileId, string fileName) =>
        $"{tenantId}/{source}/{entityType}/{entityId}/{fileId}/{fileName}";
}
