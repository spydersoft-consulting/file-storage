using Microsoft.EntityFrameworkCore;

namespace Spydersoft.FileStoreApi.Infrastructure.Data;

public sealed class FileStoreDbContext : DbContext
{
    public FileStoreDbContext(DbContextOptions<FileStoreDbContext> options) : base(options) { }

    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileEntity>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => new { f.TenantId, f.IsDeleted });
            e.HasIndex(f => new { f.TenantId, f.Source, f.EntityType, f.EntityId });
            e.HasIndex(f => new { f.Status, f.UploadExpiresAt });
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => new { d.TenantId, d.IsDeleted });
            e.HasIndex(d => new { d.TenantId, d.Source, d.EntityType, d.EntityId });
            e.HasOne(d => d.CurrentVersion)
                .WithMany()
                .HasForeignKey(d => d.CurrentVersionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DocumentVersion>(e =>
        {
            e.HasKey(v => v.Id);
            e.HasIndex(v => new { v.DocumentId, v.IsDeleted });
            e.HasOne(v => v.Document)
                .WithMany(d => d.Versions)
                .HasForeignKey(v => v.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.File)
                .WithMany(f => f.DocumentVersions)
                .HasForeignKey(v => v.FileId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
