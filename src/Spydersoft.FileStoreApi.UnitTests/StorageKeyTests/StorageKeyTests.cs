namespace Spydersoft.FileStoreApi.UnitTests.StorageKeyTests;

/// <summary>
/// Tests verifying the storage key format: {tenantId}/{source}/{entityType}/{entityId}/{fileId}/{fileName}.
/// The key drives S3 object paths, so the exact format must be stable.
/// </summary>
internal sealed class StorageKeyTests
{
    private static string BuildStorageKey(string tenantId, string source, string entityType, string entityId, Guid fileId, string fileName) =>
        $"{tenantId}/{source}/{entityType}/{entityId}/{fileId}/{fileName}";

    [Test]
    public void StorageKey_Format_MatchesExpected()
    {
        var tenantId = "tenant-abc";
        var source = "pitstop";
        var entityType = "FillUp";
        var entityId = "entity-123";
        var fileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fileName = "receipt.pdf";

        var key = BuildStorageKey(tenantId, source, entityType, entityId, fileId, fileName);

        Assert.That(key, Is.EqualTo("tenant-abc/pitstop/FillUp/entity-123/11111111-1111-1111-1111-111111111111/receipt.pdf"));
    }

    [Test]
    public void StorageKey_SegmentCount_Is6()
    {
        var key = BuildStorageKey("t1", "src", "Type", "eid", Guid.NewGuid(), "file.pdf");
        var segments = key.Split('/');

        Assert.That(segments, Has.Length.EqualTo(6));
    }

    [Test]
    public void StorageKey_TenantId_IsFirstSegment()
    {
        var tenantId = "my-tenant";
        var key = BuildStorageKey(tenantId, "src", "Type", "eid", Guid.NewGuid(), "file.pdf");

        Assert.That(key.Split('/')[0], Is.EqualTo(tenantId));
    }

    [Test]
    public void StorageKey_FileName_IsLastSegment()
    {
        var fileName = "document.docx";
        var key = BuildStorageKey("t1", "src", "Type", "eid", Guid.NewGuid(), fileName);
        var segments = key.Split('/');

        Assert.That(segments[^1], Is.EqualTo(fileName));
    }

    [Test]
    public void StorageKey_FileId_IsSecondToLastSegment()
    {
        var fileId = Guid.NewGuid();
        var key = BuildStorageKey("t1", "src", "Type", "eid", fileId, "file.pdf");
        var segments = key.Split('/');

        Assert.That(segments[^2], Is.EqualTo(fileId.ToString()));
    }

    [Test]
    public void StorageKey_WithSpecialCharactersInFileName_PreservesFileName()
    {
        // File names may contain spaces or dashes — these are stored verbatim in the key.
        var fileName = "my invoice (draft) 2024-01.pdf";
        var key = BuildStorageKey("t1", "src", "Type", "eid", Guid.NewGuid(), fileName);

        Assert.That(key, Does.EndWith($"/{fileName}"));
    }

    [Test]
    public void StorageKey_DifferentFileIds_ProduceDifferentKeys()
    {
        var fileId1 = Guid.NewGuid();
        var fileId2 = Guid.NewGuid();

        var key1 = BuildStorageKey("t1", "src", "Type", "eid", fileId1, "file.pdf");
        var key2 = BuildStorageKey("t1", "src", "Type", "eid", fileId2, "file.pdf");

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void StorageKey_DifferentTenants_ProduceDifferentKeys()
    {
        var fileId = Guid.NewGuid();
        var key1 = BuildStorageKey("tenant-a", "src", "Type", "eid", fileId, "file.pdf");
        var key2 = BuildStorageKey("tenant-b", "src", "Type", "eid", fileId, "file.pdf");

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void StorageKey_Source_IsSecondSegment()
    {
        var source = "my-app";
        var key = BuildStorageKey("t1", source, "Type", "eid", Guid.NewGuid(), "file.pdf");

        Assert.That(key.Split('/')[1], Is.EqualTo(source));
    }

    [Test]
    public void StorageKey_EntityType_IsThirdSegment()
    {
        var entityType = "PurchaseOrder";
        var key = BuildStorageKey("t1", "src", entityType, "eid", Guid.NewGuid(), "file.pdf");

        Assert.That(key.Split('/')[2], Is.EqualTo(entityType));
    }

    [Test]
    public void StorageKey_EntityId_IsFourthSegment()
    {
        var entityId = "order-99999";
        var key = BuildStorageKey("t1", "src", "Type", entityId, Guid.NewGuid(), "file.pdf");

        Assert.That(key.Split('/')[3], Is.EqualTo(entityId));
    }
}
