namespace Spydersoft.FileStore.Contracts;

public sealed record FileUrlResponse
{
    public string Url { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
}
