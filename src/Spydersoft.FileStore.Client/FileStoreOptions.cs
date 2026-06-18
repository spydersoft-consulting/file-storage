namespace Spydersoft.FileStore.Client;

public sealed class FileStoreOptions
{
    public const string SectionName = "FileStore";

    public string BaseUrl { get; set; } = string.Empty;
    public string? TokenEndpoint { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string Scope { get; set; } = "filestore:read filestore:write";
}
