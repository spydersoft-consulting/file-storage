using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;

namespace Spydersoft.FileStoreApi.Services;

public sealed class FileCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileCleanupService> _logger;

    public FileCleanupService(IServiceScopeFactory scopeFactory, ILogger<FileCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FileCleanupService starting");
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IStorageClient>();

            var expired = await db.Files
                .Where(f => f.Status == FileStatus.Pending && f.UploadExpiresAt < DateTimeOffset.UtcNow && !f.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var file in expired)
            {
                try
                {
                    await storage.DeleteObjectAsync(file.StorageKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete storage object {StorageKey}", file.StorageKey);
                }
                db.Files.Remove(file);
            }

            if (expired.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Cleaned up {Count} expired pending files", expired.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during file cleanup");
        }
    }
}
