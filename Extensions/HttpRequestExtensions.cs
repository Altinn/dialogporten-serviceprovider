using Microsoft.AspNetCore.Mvc;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Extensions;

public static class HttpRequestExtensions
{
    /// <summary>
    /// The base URL this app is reachable on for the current request, eg.
    /// <c>https://dialogporten-serviceprovider.azurewebsites.net</c>. Used as the default base for
    /// playbook callback URLs, so a playbook created through the deployed app points back at it
    /// rather than at whatever <c>ServiceProvider:mutateBaseUri</c> happens to say.
    /// </summary>
    /// <remarks>
    /// Azure App Service terminates TLS in front of Kestrel, so <c>Request.Scheme</c> is http there;
    /// the forwarded header is what tells us the browser used https.
    /// </remarks>
    public static string AppBaseUri(this ControllerBase controller) => controller.Request.AppBaseUri();

    public static string AppBaseUri(this HttpRequest request)
    {
        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() is { Length: > 0 } forwarded
            ? forwarded.Split(',')[0].Trim()
            : request.Scheme;
        return $"{scheme}://{request.Host}";
    }
}
