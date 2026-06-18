using Microsoft.EntityFrameworkCore;

namespace Spydersoft.FileStoreApi.Infrastructure.Data;

public sealed class FileStoreDbContext(DbContextOptions<FileStoreDbContext> options) : DbContext(options)
{
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentVersion>()
            .HasOne(v => v.Document)
            .WithMany(d => d.Versions)
            .HasForeignKey(v => v.DocumentId);
    }
}
