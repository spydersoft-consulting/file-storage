using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStore.Client;

public static class FileStoreServiceCollectionExtensions
{
    public static IServiceCollection AddSpydersoftFileStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStoreOptions>()
            .Bind(configuration.GetSection(FileStoreOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "FileStoreOptions.BaseUrl is required.")
            .ValidateOnStart();

        Action<IServiceProvider, HttpClient> configure = (sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<FileStoreOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
        };

        services.AddHttpClient<IFileStoreClient, FileStoreHttpClient>(configure);
        services.AddHttpClient<IDocumentClient, DocumentHttpClient>(configure);

        return services;
    }
}
