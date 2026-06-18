using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStore.Contracts;
using Spydersoft.FileStoreApi.Infrastructure.Data;

namespace Spydersoft.FileStoreApi.Services;

public sealed class RetentionPolicyService
{
    private readonly FileStoreDbContext _db;
    public RetentionPolicyService(FileStoreDbContext db) { _db = db; }

    public async Task ApplyAsync(Document document, CancellationToken cancellationToken = default)
    {
        if (document.RetentionPolicy == RetentionPolicy.KeepAll) return;

        var confirmedVersions = await _db.DocumentVersions
            .Where(v => v.DocumentId == document.Id
                     && v.Status == DocumentVersionStatus.Confirmed
                     && !v.IsDeleted)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        if (document.RetentionPolicy == RetentionPolicy.KeepN && !document.RetentionCount.HasValue)
            throw new InvalidOperationException(
                $"Document {document.Id} has RetentionPolicy=KeepN but RetentionCount is null.");

        // RetentionCount=0 would delete every version; treat it as "keep at least 1"
        // to avoid accidental total history wipe. Callers that intentionally want to
        // purge all versions should set the document to a Deleted state instead.
        var keepCount = document.RetentionPolicy switch
        {
            RetentionPolicy.KeepLatest => 1,
            RetentionPolicy.KeepN => Math.Max(1, document.RetentionCount!.Value),
            _ => confirmedVersions.Count, // fallback: keep all
        };

        IEnumerable<DocumentVersion> toDelete = confirmedVersions.Skip(keepCount);

        foreach (var version in toDelete)
        {
            version.IsDeleted = true;
            version.Status = DocumentVersionStatus.Deleted;
        }
    }
}
