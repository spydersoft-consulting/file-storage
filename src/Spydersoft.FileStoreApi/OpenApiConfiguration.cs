using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Spydersoft.FileStoreApi;

internal static class OpenApiConfiguration
{
    private const string BearerSchemeName = "bearerAuth";

    public static IServiceCollection AddFileStoreOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Spydersoft FileStore API",
                    Version = "v1",
                    Description = "Platform file storage service providing presigned URL-based blob storage and document management.",
                };
                document.Servers = [];
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT bearer token with filestore:read or filestore:write scope.",
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerSchemeName, document)] = [],
                    },
                ];
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
