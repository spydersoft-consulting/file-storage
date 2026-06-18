using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;

namespace Spydersoft.FileStoreApi.Endpoints;

internal static class FileStoreEndpoints
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
    ];

    public static IEndpointRouteBuilder MapFileStoreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/filestore").WithTags("FileStore");

        group.MapPost("/", InitiateUpload)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<InitiateUploadResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/confirm", ConfirmUpload)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListFiles)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<IReadOnlyList<FileDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetFile)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<FileDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/url", GetFileUrl)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .Produces<FileUrlResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteFile)
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> InitiateUpload(
        InitiateUploadRequest request,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        CancellationToken cancellationToken)
    {
        if (!AllowedContentTypes.Contains(request.ContentType))
        {
            return Results.Problem(
                detail: $"Content type '{request.ContentType}' is not allowed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

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

        file.StorageKey = $"{tenantId}/{request.Source}/{request.EntityType}/{request.EntityId}/{file.Id}/{request.FileName}";

        var uploadUrl = await storage.GenerateUploadUrlAsync(
            file.StorageKey, file.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        db.Files.Add(file);
        await db.SaveChangesAsync(cancellationToken);

        var response = new InitiateUploadResponse(file.Id, uploadUrl, file.UploadExpiresAt);
        return Results.Created($"/api/v1/filestore/{file.Id}", response);
    }

    private static async Task<IResult> ConfirmUpload(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var file = await db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted, cancellationToken);

        if (file is null)
            return Results.NotFound();

        var exists = await storage.ObjectExistsAsync(file.StorageKey, cancellationToken);
        if (!exists)
        {
            return Results.Problem(
                detail: "File has not been uploaded to storage yet.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        file.Status = FileStatus.Confirmed;
        file.ConfirmedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ListFiles(
        [FromQuery] string? source,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        HttpContext context,
        FileStoreDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var query = db.Files
            .Where(f => f.TenantId == tenantId && !f.IsDeleted && f.Status == FileStatus.Confirmed);

        if (!string.IsNullOrEmpty(source))
            query = query.Where(f => f.Source == source);
        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(f => f.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId))
            query = query.Where(f => f.EntityId == entityId);

        var files = await query
            .Select(f => new FileDto(
                f.Id, f.TenantId, f.Source, f.EntityType, f.EntityId,
                f.FileName, f.ContentType, f.SizeBytes, f.Status,
                f.InitiatedAt, f.ConfirmedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(files);
    }

    private static async Task<IResult> GetFile(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var file = await db.Files
            .Where(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted)
            .Select(f => ToDto(f))
            .FirstOrDefaultAsync(cancellationToken);

        return file is null ? Results.NotFound() : Results.Ok(file);
    }

    private static async Task<IResult> GetFileUrl(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        IStorageClient storage,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var file = await db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted && f.Status == FileStatus.Confirmed, cancellationToken);

        if (file is null)
            return Results.NotFound();

        var ttl = TimeSpan.FromHours(1);
        var url = await storage.GenerateDownloadUrlAsync(file.StorageKey, ttl, cancellationToken);
        return Results.Ok(new FileUrlResponse(url, DateTimeOffset.UtcNow.Add(ttl)));
    }

    private static async Task<IResult> DeleteFile(
        Guid id,
        HttpContext context,
        FileStoreDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var file = await db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted, cancellationToken);

        if (file is null)
            return Results.NotFound();

        file.IsDeleted = true;
        file.Status = FileStatus.Deleted;
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static string GetTenantId(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value
        ?? string.Empty;

    private static FileDto ToDto(FileEntity f) => new(
        f.Id, f.TenantId, f.Source, f.EntityType, f.EntityId,
        f.FileName, f.ContentType, f.SizeBytes, f.Status,
        f.InitiatedAt, f.ConfirmedAt);
}
