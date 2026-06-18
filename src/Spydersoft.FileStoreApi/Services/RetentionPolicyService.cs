using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;

namespace Spydersoft.FileStoreApi.Services;

public sealed class RetentionPolicyService
{
    private readonly FileStoreDbContext _db;

    public RetentionPolicyService(FileStoreDbContext db)
    {
        _db = db;
    }

    public async Task ApplyAsync(Document document, CancellationToken cancellationToken = default)
    {
        if (document.RetentionPolicy == RetentionPolicy.KeepAll)
            return;

        var confirmedVersions = await _db.DocumentVersions
            .Where(v => v.DocumentId == document.Id
                     && v.Status == DocumentVersionStatus.Confirmed
                     && !v.IsDeleted)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        IEnumerable<DocumentVersion> toDelete = document.RetentionPolicy switch
        {
            RetentionPolicy.KeepLatest => confirmedVersions.Skip(1),
            RetentionPolicy.KeepN when document.RetentionCount.HasValue => confirmedVersions.Skip(document.RetentionCount.Value),
            _ => [],
        };

        foreach (var version in toDelete)
        {
            version.IsDeleted = true;
            version.Status = DocumentVersionStatus.Deleted;
        }
    }
}
