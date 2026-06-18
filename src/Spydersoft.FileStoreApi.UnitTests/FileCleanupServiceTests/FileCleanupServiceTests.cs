using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;
using Spydersoft.FileStoreApi.Services;

namespace Spydersoft.FileStoreApi.UnitTests.FileCleanupServiceTests;

/// <summary>
/// Tests for <see cref="FileCleanupService.RunCleanupAsync"/>.
/// Uses EF Core InMemory and NSubstitute for the storage client.
///
/// Test pattern: each test shares a named InMemory database via DbContextOptions,
/// so scopes inside RunCleanupAsync get their own DbContext instances (matching
/// production's Scoped lifetime) but all operate on the same in-memory store.
/// Assertions query a fresh DbContext to bypass the EF identity map.
/// </summary>
internal sealed class FileCleanupServiceTests
{
    private static DbContextOptions<FileStoreDbContext> CreateDbOptions(string dbName) =>
        new DbContextOptionsBuilder<FileStoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    private static FileStoreDbContext OpenDb(DbContextOptions<FileStoreDbContext> options) =>
        new(options);

    /// <summary>
    /// Builds a minimal <see cref="IServiceScopeFactory"/> backed by the given
    /// <paramref name="dbOptions"/>. Each scope creates a fresh DbContext instance
    /// that shares the same in-memory store — matching EF Core's Scoped lifetime.
    /// </summary>
    private static IServiceScopeFactory BuildScopeFactory(
        DbContextOptions<FileStoreDbContext> dbOptions, IStorageClient storage)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new FileStoreDbContext(dbOptions));
        services.AddSingleton(storage);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static FileEntity CreatePendingExpiredFile(string tenantId = "t1") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Source = "test",
            EntityType = "Invoice",
            EntityId = "e1",
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
            StorageKey = $"t1/test/Invoice/e1/{Guid.NewGuid()}/invoice.pdf",
            Status = FileStatus.Pending,
            InitiatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            UploadExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), // expired
            SizeBytes = 0,
        };

    private static FileEntity CreateConfirmedFile(string tenantId = "t1") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Source = "test",
            EntityType = "Invoice",
            EntityId = "e1",
            FileName = "confirmed.pdf",
            ContentType = "application/pdf",
            StorageKey = $"t1/test/Invoice/e1/{Guid.NewGuid()}/confirmed.pdf",
            Status = FileStatus.Confirmed,
            InitiatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ConfirmedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UploadExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            SizeBytes = 1024,
        };

    private static FileEntity CreatePendingNonExpiredFile(string tenantId = "t1") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Source = "test",
            EntityType = "Invoice",
            EntityId = "e1",
            FileName = "pending.pdf",
            ContentType = "application/pdf",
            StorageKey = $"t1/test/Invoice/e1/{Guid.NewGuid()}/pending.pdf",
            Status = FileStatus.Pending,
            InitiatedAt = DateTimeOffset.UtcNow,
            UploadExpiresAt = DateTimeOffset.UtcNow.AddHours(1), // not yet expired
            SizeBytes = 0,
        };

    [Test]
    public async Task CleanupService_DeletesExpiredPendingFiles()
    {
        var dbOptions = CreateDbOptions(Guid.NewGuid().ToString());
        var storage = Substitute.For<IStorageClient>();
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var expiredFile = CreatePendingExpiredFile();
        using (var db = OpenDb(dbOptions))
        {
            db.Files.Add(expiredFile);
            await db.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory(dbOptions, storage);
        var sut = new FileCleanupService(scopeFactory, NullLogger<FileCleanupService>.Instance);

        await sut.RunCleanupAsync();

        using var assertDb = OpenDb(dbOptions);
        var file = await assertDb.Files.AsNoTracking().FirstAsync(f => f.Id == expiredFile.Id);
        Assert.That(file.IsDeleted, Is.True);
        Assert.That(file.Status, Is.EqualTo(FileStatus.Deleted));
        await storage.Received(1).DeleteAsync(expiredFile.StorageKey, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CleanupService_DoesNotDeleteConfirmedFiles()
    {
        var dbOptions = CreateDbOptions(Guid.NewGuid().ToString());
        var storage = Substitute.For<IStorageClient>();

        var confirmedFile = CreateConfirmedFile();
        using (var db = OpenDb(dbOptions))
        {
            db.Files.Add(confirmedFile);
            await db.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory(dbOptions, storage);
        var sut = new FileCleanupService(scopeFactory, NullLogger<FileCleanupService>.Instance);

        await sut.RunCleanupAsync();

        using var assertDb = OpenDb(dbOptions);
        var file = await assertDb.Files.AsNoTracking().FirstAsync(f => f.Id == confirmedFile.Id);
        Assert.That(file.IsDeleted, Is.False);
        Assert.That(file.Status, Is.EqualTo(FileStatus.Confirmed));
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CleanupService_DoesNotDeleteNonExpiredPendingFiles()
    {
        var dbOptions = CreateDbOptions(Guid.NewGuid().ToString());
        var storage = Substitute.For<IStorageClient>();

        var pendingFile = CreatePendingNonExpiredFile();
        using (var db = OpenDb(dbOptions))
        {
            db.Files.Add(pendingFile);
            await db.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory(dbOptions, storage);
        var sut = new FileCleanupService(scopeFactory, NullLogger<FileCleanupService>.Instance);

        await sut.RunCleanupAsync();

        using var assertDb = OpenDb(dbOptions);
        var file = await assertDb.Files.AsNoTracking().FirstAsync(f => f.Id == pendingFile.Id);
        Assert.That(file.IsDeleted, Is.False);
        Assert.That(file.Status, Is.EqualTo(FileStatus.Pending));
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CleanupService_SkipsFileNotInStorage_StillMarksDeleted()
    {
        var dbOptions = CreateDbOptions(Guid.NewGuid().ToString());
        var storage = Substitute.For<IStorageClient>();
        // Storage doesn't have the file (upload never completed)
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var expiredFile = CreatePendingExpiredFile();
        using (var db = OpenDb(dbOptions))
        {
            db.Files.Add(expiredFile);
            await db.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory(dbOptions, storage);
        var sut = new FileCleanupService(scopeFactory, NullLogger<FileCleanupService>.Instance);

        await sut.RunCleanupAsync();

        using var assertDb = OpenDb(dbOptions);
        var file = await assertDb.Files.AsNoTracking().FirstAsync(f => f.Id == expiredFile.Id);
        Assert.That(file.IsDeleted, Is.True);
        Assert.That(file.Status, Is.EqualTo(FileStatus.Deleted));
        // DeleteAsync should NOT be called when file doesn't exist in storage
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CleanupService_MixedFiles_OnlyDeletesExpiredPending()
    {
        var dbOptions = CreateDbOptions(Guid.NewGuid().ToString());
        var storage = Substitute.For<IStorageClient>();
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var expiredFile = CreatePendingExpiredFile();
        var confirmedFile = CreateConfirmedFile();
        var nonExpiredFile = CreatePendingNonExpiredFile();

        using (var db = OpenDb(dbOptions))
        {
            db.Files.AddRange(expiredFile, confirmedFile, nonExpiredFile);
            await db.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory(dbOptions, storage);
        var sut = new FileCleanupService(scopeFactory, NullLogger<FileCleanupService>.Instance);

        await sut.RunCleanupAsync();

        using var assertDb = OpenDb(dbOptions);
        var expired = await assertDb.Files.AsNoTracking().FirstAsync(f => f.Id == expiredFile.Id);
        var confirmed = await assertDb.Files.AsNoTracking().FirstAsync(f => f.Id == confirmedFile.Id);
        var nonExpired = await assertDb.Files.AsNoTracking().FirstAsync(f => f.Id == nonExpiredFile.Id);

        Assert.That(expired.IsDeleted, Is.True, "Expired pending file should be deleted");
        Assert.That(confirmed.IsDeleted, Is.False, "Confirmed file should not be deleted");
        Assert.That(nonExpired.IsDeleted, Is.False, "Non-expired pending file should not be deleted");

        await storage.Received(1).DeleteAsync(expiredFile.StorageKey, Arg.Any<CancellationToken>());
    }
}
