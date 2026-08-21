namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public static class FceMediaTypes
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/markdown",
        "text/plain",
        "text/html"
    };

    public static bool IsAllowed(string? mediaType) =>
        mediaType is not null && Allowed.Contains(mediaType);
}
