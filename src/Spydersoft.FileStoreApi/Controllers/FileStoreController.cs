using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;

namespace Spydersoft.FileStoreApi.Controllers;

[ApiController]
[Route("api/v1/filestore")]
[Tags("FileStore")]
public class FileStoreController(FileStoreDbContext db, IStorageClient storage) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
    ];

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(typeof(InitiateUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiateUpload(InitiateUploadRequest request, CancellationToken cancellationToken)
    {
        if (!AllowedContentTypes.Contains(request.ContentType))
            return Problem(detail: $"Content type '{request.ContentType}' is not allowed.", statusCode: StatusCodes.Status400BadRequest);

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
        file.StorageKey = $"{tenantId}/{request.Source}/{request.EntityType}/{request.EntityId}/{file.Id}/{request.FileName}";

        var uploadUrl = await storage.GenerateUploadUrlAsync(file.StorageKey, file.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        db.Files.Add(file);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"/api/v1/filestore/{file.Id}", new InitiateUploadResponse(file.Id, uploadUrl, file.UploadExpiresAt));
    }

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmUpload(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted, cancellationToken);
        if (file is null)
            return NotFound();

        var exists = await storage.ObjectExistsAsync(file.StorageKey, cancellationToken);
        if (!exists)
            return Problem(detail: "File has not been uploaded to storage yet.", statusCode: StatusCodes.Status422UnprocessableEntity);

        file.Status = FileStatus.Confirmed;
        file.ConfirmedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<FileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFiles(
        [FromQuery] string? source,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var query = db.Files.Where(f => f.TenantId == tenantId && !f.IsDeleted && f.Status == FileStatus.Confirmed);

        if (!string.IsNullOrEmpty(source))
            query = query.Where(f => f.Source == source);
        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(f => f.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId))
            query = query.Where(f => f.EntityId == entityId);

        var files = await query
            .Select(f => new FileDto(f.Id, f.TenantId, f.Source, f.EntityType, f.EntityId, f.FileName, f.ContentType, f.SizeBytes, f.Status, f.InitiatedAt, f.ConfirmedAt))
            .ToListAsync(cancellationToken);

        return Ok(files);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var file = await db.Files
            .Where(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted)
            .Select(f => new FileDto(f.Id, f.TenantId, f.Source, f.EntityType, f.EntityId, f.FileName, f.ContentType, f.SizeBytes, f.Status, f.InitiatedAt, f.ConfirmedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return file is null ? NotFound() : Ok(file);
    }

    [HttpGet("{id:guid}/url")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType(typeof(FileUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileUrl(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted && f.Status == FileStatus.Confirmed, cancellationToken);
        if (file is null)
            return NotFound();

        var ttl = TimeSpan.FromHours(1);
        var url = await storage.GenerateDownloadUrlAsync(file.StorageKey, ttl, cancellationToken);
        return Ok(new FileUrlResponse(url, DateTimeOffset.UtcNow.Add(ttl)));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId && !f.IsDeleted, cancellationToken);
        if (file is null)
            return NotFound();

        file.IsDeleted = true;
        file.Status = FileStatus.Deleted;
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private string GetTenantId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? string.Empty;
}
