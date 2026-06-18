using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;

namespace Spydersoft.FileStoreApi.Services;

public sealed class FileCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<FileCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    public async Task RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageClient>();

        var expiredFiles = await db.Files
            .Where(f => f.Status == FileStatus.Pending
                     && f.UploadExpiresAt < DateTimeOffset.UtcNow
                     && !f.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var file in expiredFiles)
        {
            // Mark deleted in the DB regardless of whether the storage deletion
            // succeeds, to prevent infinite retries on transient storage errors.
            // A storage orphan is preferable to a stuck-Pending file.
            file.IsDeleted = true;
            file.Status = FileStatus.Deleted;

            try
            {
                if (await storage.ExistsAsync(file.StorageKey, cancellationToken))
                {
                    await storage.DeleteAsync(file.StorageKey, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete storage object for expired file {FileId} — DB record marked deleted; storage object may be orphaned", file.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
