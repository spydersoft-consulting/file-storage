using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Services;

namespace Spydersoft.FileStoreApi.UnitTests.RetentionPolicyServiceTests;

/// <summary>
/// Tests for <see cref="RetentionPolicyService.ApplyAsync"/>.
/// Each test uses an isolated in-memory database so version state never leaks across cases.
/// </summary>
internal sealed class RetentionPolicyServiceTests
{
    private static FileStoreDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FileStoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Document CreateDocument(Guid id, RetentionPolicy policy, int? retentionCount = null) =>
        new()
        {
            Id = id,
            TenantId = "tenant-1",
            Source = "test",
            EntityType = "Invoice",
            EntityId = "entity-1",
            Name = "Test Document",
            RetentionPolicy = policy,
            RetentionCount = retentionCount,
        };

    private static DocumentVersion CreateConfirmedVersion(Guid documentId, int versionNumber) =>
        new()
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            FileId = Guid.NewGuid(),
            VersionNumber = versionNumber,
            Status = DocumentVersionStatus.Confirmed,
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-versionNumber),
        };

    [Test]
    public async Task KeepAll_DoesNotDeleteAnyVersions()
    {
        var docId = Guid.NewGuid();
        var document = CreateDocument(docId, RetentionPolicy.KeepAll);

        using var db = CreateDb();
        db.Documents.Add(document);
        db.DocumentVersions.AddRange(
            CreateConfirmedVersion(docId, 1),
            CreateConfirmedVersion(docId, 2),
            CreateConfirmedVersion(docId, 3));
        await db.SaveChangesAsync();

        var sut = new RetentionPolicyService(db);
        await sut.ApplyAsync(document);

        var allVersions = await db.DocumentVersions.Where(v => v.DocumentId == docId).ToListAsync();
        Assert.That(allVersions, Has.All.Matches<DocumentVersion>(v => !v.IsDeleted));
        Assert.That(allVersions, Has.All.Matches<DocumentVersion>(v => v.Status == DocumentVersionStatus.Confirmed));
    }

    [Test]
    public async Task KeepLatest_DeletesOlderVersions_KeepsNewest()
    {
        var docId = Guid.NewGuid();
        var document = CreateDocument(docId, RetentionPolicy.KeepLatest);

        using var db = CreateDb();
        db.Documents.Add(document);
        db.DocumentVersions.AddRange(
            CreateConfirmedVersion(docId, 1),
            CreateConfirmedVersion(docId, 2),
            CreateConfirmedVersion(docId, 3));
        await db.SaveChangesAsync();

        var sut = new RetentionPolicyService(db);
        await sut.ApplyAsync(document);

        var versions = await db.DocumentVersions
            .Where(v => v.DocumentId == docId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();

        // Version 3 (highest) should survive
        var newest = versions.First();
        Assert.That(newest.VersionNumber, Is.EqualTo(3));
        Assert.That(newest.IsDeleted, Is.False);
        Assert.That(newest.Status, Is.EqualTo(DocumentVersionStatus.Confirmed));

        // Versions 1 and 2 should be marked deleted
        var older = versions.Skip(1).ToList();
        Assert.That(older, Has.Count.EqualTo(2));
        Assert.That(older, Has.All.Matches<DocumentVersion>(v => v.IsDeleted));
        Assert.That(older, Has.All.Matches<DocumentVersion>(v => v.Status == DocumentVersionStatus.Deleted));
    }

    [Test]
    public async Task KeepLatest_WithSingleVersion_DoesNotDelete()
    {
        var docId = Guid.NewGuid();
        var document = CreateDocument(docId, RetentionPolicy.KeepLatest);

        using var db = CreateDb();
        db.Documents.Add(document);
        db.DocumentVersions.Add(CreateConfirmedVersion(docId, 1));
        await db.SaveChangesAsync();

        var sut = new RetentionPolicyService(db);
        await sut.ApplyAsync(document);

        var version = await db.DocumentVersions.SingleAsync(v => v.DocumentId == docId);
        Assert.That(version.IsDeleted, Is.False);
        Assert.That(version.Status, Is.EqualTo(DocumentVersionStatus.Confirmed));
    }

    [Test]
    public async Task KeepN_3_With5Versions_Keeps3Newest()
    {
        var docId = Guid.NewGuid();
        var document = CreateDocument(docId, RetentionPolicy.KeepN, retentionCount: 3);

        using var db = CreateDb();
        db.Documents.Add(document);
        db.DocumentVersions.AddRange(
            CreateConfirmedVersion(docId, 1),
            CreateConfirmedVersion(docId, 2),
            CreateConfirmedVersion(docId, 3),
            CreateConfirmedVersion(docId, 4),
            CreateConfirmedVersion(docId, 5));
        await db.SaveChangesAsync();

        var sut = new RetentionPolicyService(db);
        await sut.ApplyAsync(document);

        var versions = await db.DocumentVersions
            .Where(v => v.DocumentId == docId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();

        // Top 3 (versions 5, 4, 3) should survive
        var kept = versions.Take(3).ToList();
        Assert.That(kept.Select(v => v.VersionNumber), Is.EquivalentTo(new[] { 5, 4, 3 }));
        Assert.That(kept, Has.All.Matches<DocumentVersion>(v => !v.IsDeleted));

        // Versions 1 and 2 should be deleted
        var deleted = versions.Skip(3).ToList();
        Assert.That(deleted, Has.Count.EqualTo(2));
        Assert.That(deleted, Has.All.Matches<DocumentVersion>(v => v.IsDeleted));
    }

    [Test]
    public async Task KeepN_DeletesVersionsMarkAsDeleted()
    {
        var docId = Guid.NewGuid();
        var document = CreateDocument(docId, RetentionPolicy.KeepN, retentionCount: 1);

        using var db = CreateDb();
        db.Documents.Add(document);
        db.DocumentVersions.AddRange(
            CreateConfirmedVersion(docId, 1),
            CreateConfirmedVersion(docId, 2),
            CreateConfirmedVersion(docId, 3));
        await db.SaveChangesAsync();

        var sut = new RetentionPolicyService(db);
        await sut.ApplyAsync(document);

        // ApplyAsync mutates tracked entities; the caller is responsible for saving.
        // Save here to simulate what the real caller does, then clear the identity map
        // so the assertion round-trips to the store rather than the cached objects.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toBeDeleted = await db.DocumentVersions
            .Where(v => v.DocumentId == docId && v.VersionNumber != 3)
            .ToListAsync();

        Assert.That(toBeDeleted, Has.All.Matches<DocumentVersion>(v => v.IsDeleted), "IsDeleted must be true");
        Assert.That(toBeDeleted, Has.All.Matches<DocumentVersion>(v => v.Status == DocumentVersionStatus.Deleted), "Status must be Deleted");
    }

    [Test]
    public void KeepN_WithNullRetentionCount_ThrowsInvalidOperationException()
    {
        var docId = Guid.NewGuid();
        var document = CreateDocument(docId, RetentionPolicy.KeepN, retentionCount: null);

        using var db = CreateDb();
        var sut = new RetentionPolicyService(db);

        Assert.That(
            async () => await sut.ApplyAsync(document),
            Throws.InvalidOperationException.With.Message.Contains("RetentionCount is null"));
    }

    [Test]
    public async Task KeepLatest_IgnoresPendingAndAlreadyDeletedVersions()
    {
        var docId = Guid.NewGuid();
        var document = CreateDocument(docId, RetentionPolicy.KeepLatest);

        using var db = CreateDb();
        db.Documents.Add(document);

        // Confirmed versions that policy should evaluate
        db.DocumentVersions.Add(CreateConfirmedVersion(docId, 2));
        db.DocumentVersions.Add(CreateConfirmedVersion(docId, 3));

        // Pending version — should not be touched by retention
        db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            FileId = Guid.NewGuid(),
            VersionNumber = 4,
            Status = DocumentVersionStatus.Pending,
            UploadedAt = DateTimeOffset.UtcNow,
        });

        // Already-deleted version — should not be re-processed
        db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            FileId = Guid.NewGuid(),
            VersionNumber = 1,
            Status = DocumentVersionStatus.Deleted,
            IsDeleted = true,
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-10),
        });

        await db.SaveChangesAsync();

        var sut = new RetentionPolicyService(db);
        await sut.ApplyAsync(document);

        // Version 3 (newest confirmed) should survive untouched
        var v3 = await db.DocumentVersions.SingleAsync(v => v.DocumentId == docId && v.VersionNumber == 3);
        Assert.That(v3.IsDeleted, Is.False);

        // Version 2 (older confirmed) should be marked deleted
        var v2 = await db.DocumentVersions.SingleAsync(v => v.DocumentId == docId && v.VersionNumber == 2);
        Assert.That(v2.IsDeleted, Is.True);

        // Pending version 4 should be untouched (still Pending)
        var v4 = await db.DocumentVersions.SingleAsync(v => v.DocumentId == docId && v.VersionNumber == 4);
        Assert.That(v4.Status, Is.EqualTo(DocumentVersionStatus.Pending));
        Assert.That(v4.IsDeleted, Is.False);
    }
}
