using Microsoft.EntityFrameworkCore;
using Spydersoft.FileStoreApi.Infrastructure.Data;

namespace Spydersoft.FileStoreApi.Endpoints;

internal static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/test").WithTags("Test");

        group.MapDelete("/filestore", async (
            string source,
            FileStoreDbContext db,
            CancellationToken ct) =>
        {
            // Null out CurrentVersionId FK before deleting versions to avoid constraint violations
            await db.Documents
                .Where(d => d.Source == source)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.CurrentVersionId, (Guid?)null), ct);

            var docIds = db.Documents.Where(d => d.Source == source).Select(d => d.Id);
            await db.DocumentVersions
                .Where(v => docIds.Contains(v.DocumentId))
                .ExecuteDeleteAsync(ct);

            await db.Documents.Where(d => d.Source == source).ExecuteDeleteAsync(ct);
            await db.Files.Where(f => f.Source == source).ExecuteDeleteAsync(ct);

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
