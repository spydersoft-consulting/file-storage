using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spydersoft.FileStore.Contracts;

namespace Spydersoft.FileStore.Client;

public static class FileStoreServiceCollectionExtensions
{
    /// <summary>Name of the plain (unauthenticated) HttpClient used to request client-credentials tokens.</summary>
    internal const string TokenClientName = "Spydersoft.FileStore.TokenClient";

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

        services.AddHttpClient(TokenClientName);
        services.AddTransient<ClientCredentialsTokenHandler>();

        services.AddHttpClient<IFileStoreClient, FileStoreHttpClient>(configure)
            .AddHttpMessageHandler<ClientCredentialsTokenHandler>();
        services.AddHttpClient<IDocumentClient, DocumentHttpClient>(configure)
            .AddHttpMessageHandler<ClientCredentialsTokenHandler>();

        return services;
    }
}
