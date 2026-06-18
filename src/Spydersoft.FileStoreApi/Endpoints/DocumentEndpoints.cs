using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;
using Spydersoft.FileStoreApi.Services;

namespace Spydersoft.FileStoreApi.Endpoints;

internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/documents").WithTags("Documents");

        group.MapPost("/", CreateDocument)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<CreateDocumentResponse>(StatusCodes.Status201Created);

        group.MapPost("/{id:guid}/versions", AddVersion)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<AddVersionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/versions/{vId:guid}/confirm", ConfirmVersion)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListDocuments)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetDocument)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/url", GetDocumentUrl)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<FileUrlResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/versions", ListVersions)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<IReadOnlyList<DocumentVersionDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/versions/{vId:guid}", GetVersion)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<DocumentVersionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/versions/{vId:guid}/url", GetVersionUrl)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<FileUrlResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteDocument)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/versions/{vId:guid}", DeleteVersion)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateDocument(
        CreateDocumentRequest request,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        AuditEventService audit,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

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
        var uploadUrl = await storage.GenerateUploadUrlAsync(
            file.StorageKey, file.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

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

        var response = new CreateDocumentResponse(document.Id, version.Id, file.Id, uploadUrl, file.UploadExpiresAt);
        return Results.Created($"/api/v1/documents/{document.Id}", response);
    }

    private static async Task<IResult> AddVersion(
        Guid id,
        AddVersionRequest request,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (document is null)
            return Results.NotFound();

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
        var uploadUrl = await storage.GenerateUploadUrlAsync(
            file.StorageKey, file.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

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

        var response = new AddVersionResponse(version.Id, file.Id, uploadUrl, file.UploadExpiresAt);
        return Results.Created($"/api/v1/documents/{document.Id}/versions/{version.Id}", response);
    }

    private static async Task<IResult> ConfirmVersion(
        Guid id,
        Guid vId,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        RetentionPolicyService retention,
        AuditEventService audit,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (document is null)
            return Results.NotFound();

        var version = await db.DocumentVersions
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted, cancellationToken);

        if (version is null)
            return Results.NotFound();

        var exists = await storage.ObjectExistsAsync(version.File.StorageKey, cancellationToken);
        if (!exists)
        {
            return Results.Problem(
                detail: "File has not been uploaded to storage yet.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        version.Status = DocumentVersionStatus.Confirmed;
        version.File.Status = FileStatus.Confirmed;
        version.File.ConfirmedAt = DateTimeOffset.UtcNow;

        document.CurrentVersionId = version.Id;

        await retention.ApplyAsync(document, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await audit.EmitVersionConfirmedAsync(document.Id, version.Id, tenantId, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ListDocuments(
        [FromQuery] string? source,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        HttpContext context,
        FileStoreDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var query = db.Documents
            .Where(d => d.TenantId == tenantId && !d.IsDeleted);

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

        return Results.Ok(documents);
    }

    private static async Task<IResult> GetDocument(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

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

        return document is null ? Results.NotFound() : Results.Ok(document);
    }

    private static async Task<IResult> GetDocumentUrl(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var document = await db.Documents
            .Include(d => d.CurrentVersion)
                .ThenInclude(v => v!.File)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (document is null || document.CurrentVersion is null)
            return Results.NotFound();

        var ttl = TimeSpan.FromHours(1);
        var url = await storage.GenerateDownloadUrlAsync(document.CurrentVersion.File.StorageKey, ttl, cancellationToken);
        return Results.Ok(new FileUrlResponse(url, DateTimeOffset.UtcNow.Add(ttl)));
    }

    private static async Task<IResult> ListVersions(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var exists = await db.Documents
            .AnyAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (!exists)
            return Results.NotFound();

        var versions = await db.DocumentVersions
            .Where(v => v.DocumentId == id && !v.IsDeleted)
            .OrderBy(v => v.VersionNumber)
            .Select(v => new DocumentVersionDto(
                v.Id, v.DocumentId, v.FileId, v.VersionNumber, v.Comment,
                v.Status, v.UploadedAt, v.UploadedBy))
            .ToListAsync(cancellationToken);

        return Results.Ok(versions);
    }

    private static async Task<IResult> GetVersion(
        Guid id,
        Guid vId,
        HttpContext context,
        FileStoreDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var exists = await db.Documents
            .AnyAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (!exists)
            return Results.NotFound();

        var version = await db.DocumentVersions
            .Where(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted)
            .Select(v => new DocumentVersionDto(
                v.Id, v.DocumentId, v.FileId, v.VersionNumber, v.Comment,
                v.Status, v.UploadedAt, v.UploadedBy))
            .FirstOrDefaultAsync(cancellationToken);

        return version is null ? Results.NotFound() : Results.Ok(version);
    }

    private static async Task<IResult> GetVersionUrl(
        Guid id,
        Guid vId,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var exists = await db.Documents
            .AnyAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (!exists)
            return Results.NotFound();

        var version = await db.DocumentVersions
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted, cancellationToken);

        if (version is null)
            return Results.NotFound();

        var ttl = TimeSpan.FromHours(1);
        var url = await storage.GenerateDownloadUrlAsync(version.File.StorageKey, ttl, cancellationToken);
        return Results.Ok(new FileUrlResponse(url, DateTimeOffset.UtcNow.Add(ttl)));
    }

    private static async Task<IResult> DeleteDocument(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        AuditEventService audit,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var document = await db.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (document is null)
            return Results.NotFound();

        document.IsDeleted = true;
        foreach (var version in document.Versions.Where(v => !v.IsDeleted))
        {
            version.IsDeleted = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.EmitDocumentDeletedAsync(document.Id, tenantId, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteVersion(
        Guid id,
        Guid vId,
        HttpContext context,
        FileStoreDbContext db,
        AuditEventService audit,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && !d.IsDeleted, cancellationToken);

        if (document is null)
            return Results.NotFound();

        var version = await db.DocumentVersions
            .FirstOrDefaultAsync(v => v.Id == vId && v.DocumentId == id && !v.IsDeleted, cancellationToken);

        if (version is null)
            return Results.NotFound();

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

        return Results.NoContent();
    }

    private static string GetTenantId(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value
        ?? string.Empty;

    private static string BuildStorageKey(string tenantId, string source, string entityType, string entityId, Guid fileId, string fileName) =>
        $"{tenantId}/{source}/{entityType}/{entityId}/{fileId}/{fileName}";
}
