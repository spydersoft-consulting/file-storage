namespace Spydersoft.FileStore.Client;

internal static class HttpClientHelpers
{
    internal static string BuildQuery(IEnumerable<(string Key, string? Value)> parameters)
    {
        var parts = parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");
        var qs = string.Join("&", parts);
        return string.IsNullOrEmpty(qs) ? string.Empty : "?" + qs;
    }
}
