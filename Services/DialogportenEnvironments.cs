namespace Digdir.BDB.Dialogporten.ServiceProvider.Services;

/// <summary>
/// Bound from the <c>DialogportenEnvironments</c> configuration section. Describes every
/// Dialogporten/Arbeidsflate environment this service provider can talk to, so a playbook can be
/// created in TT02, AT23 or a locally running Dialogporten without redeploying with new settings.
/// </summary>
public sealed class DialogportenEnvironmentsSettings
{
    /// <summary>Key of the environment used when a request does not specify one.</summary>
    public string Default { get; set; } = null!;

    public Dictionary<string, DialogportenEnvironmentSettings> Environments { get; set; } = new();
}

public sealed class DialogportenEnvironmentSettings
{
    /// <summary>Label shown in the GUI. Defaults to the upper-cased key.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Dialogporten base URI, up to but excluding <c>/api/v1</c>,
    /// eg. <c>https://platform.tt02.altinn.no/dialogporten</c>.
    /// </summary>
    public string DialogportenBaseUri { get; set; } = null!;

    /// <summary>Arbeidsflate base URI, used to build the "open in inbox" link.</summary>
    public string AfUri { get; set; } = null!;

    /// <summary>
    /// Optional pin for the base URI this environment's playbook callback URLs (GUI actions, FCEs)
    /// point at. Leave unset to use the URL the app itself is being browsed on — see
    /// <see cref="DialogportenEnvironment.ResolveCallbackBaseUri"/>.
    /// </summary>
    public string? MutateBaseUri { get; set; }

    /// <summary>
    /// Whether service owner API calls to this environment should carry a Maskinporten token.
    /// Set to false for a local Dialogporten running with authentication disabled.
    /// </summary>
    public bool UseMaskinporten { get; set; } = true;
}

/// <summary>
/// A validated, resolved environment. <see cref="HttpClientName"/> is the name of the named
/// <see cref="HttpClient"/> registered for this environment in <c>Program.cs</c>.
/// </summary>
public sealed record DialogportenEnvironment(
    string Key,
    string DisplayName,
    string DialogportenBaseUri,
    string AfUri,
    string? MutateBaseUri,
    bool UseMaskinporten)
{
    public string HttpClientName => $"dialogporten-env-{Key}";

    public string JwksUri => $"{DialogportenBaseUri.TrimEnd('/')}/api/v1/.well-known/jwks.json";

    public string InboxUrl(Guid dialogId) => $"{AfUri.TrimEnd('/')}/inbox/{dialogId}";

    /// <summary>
    /// Base URI a playbook's GUI action and FCE URLs point back at, ie. where this service provider
    /// is reachable from the browser showing Arbeidsflate. In order: this environment's
    /// <c>MutateBaseUri</c>, then <c>ServiceProvider:mutateBaseUri</c>, then the URL the app itself
    /// is being reached on — the last being what makes a playbook created through the deployed app
    /// call back to the deployed app instead of to some configured localhost.
    /// </summary>
    public string ResolveCallbackBaseUri(string? configuredFallback, string appBaseUri)
    {
        var resolved = new[] { MutateBaseUri, configuredFallback, appBaseUri }
            .First(x => !string.IsNullOrWhiteSpace(x))!;
        return resolved.Trim().TrimEnd('/');
    }

    /// <summary>
    /// Dialogporten rejects GUI action URLs that are not valid https, and every playbook button is
    /// one, so a bad callback base is reported before the create call rather than as an opaque 400.
    /// </summary>
    public static string? ValidateCallbackBaseUri(string callbackBaseUri)
    {
        if (!Uri.TryCreate(callbackBaseUri, UriKind.Absolute, out var uri))
        {
            return $"Callback base URI '{callbackBaseUri}' is not an absolute URL. Set ServiceProvider:mutateBaseUri or the environment's MutateBaseUri.";
        }
        return uri.Scheme == Uri.UriSchemeHttps
            ? null
            : $"Callback base URI '{callbackBaseUri}' must be https — Dialogporten rejects GUI action URLs that are not https. Browse the app over https, or pin ServiceProvider:mutateBaseUri.";
    }
}

public interface IDialogportenEnvironmentRegistry
{
    IReadOnlyList<DialogportenEnvironment> All { get; }
    DialogportenEnvironment Default { get; }

    /// <summary>Resolves a key case-insensitively. Null/whitespace yields <see cref="Default"/>.</summary>
    bool TryResolve(string? key, out DialogportenEnvironment environment);

    /// <summary>As <see cref="TryResolve"/>, but throws on an unknown key.</summary>
    DialogportenEnvironment Resolve(string? key);
}

public sealed class DialogportenEnvironmentRegistry : IDialogportenEnvironmentRegistry
{
    private readonly Dictionary<string, DialogportenEnvironment> _byKey;

    public IReadOnlyList<DialogportenEnvironment> All { get; }
    public DialogportenEnvironment Default { get; }

    public DialogportenEnvironmentRegistry(DialogportenEnvironmentsSettings settings)
    {
        if (settings.Environments.Count == 0)
        {
            throw new InvalidOperationException(
                "DialogportenEnvironments:Environments is empty. At least one environment must be configured.");
        }

        _byKey = new Dictionary<string, DialogportenEnvironment>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, env) in settings.Environments)
        {
            if (string.IsNullOrWhiteSpace(env.DialogportenBaseUri) || string.IsNullOrWhiteSpace(env.AfUri))
            {
                throw new InvalidOperationException(
                    $"DialogportenEnvironments:Environments:{key} must set both DialogportenBaseUri and AfUri.");
            }
            if (!Uri.IsWellFormedUriString(env.DialogportenBaseUri, UriKind.Absolute))
            {
                throw new InvalidOperationException(
                    $"DialogportenEnvironments:Environments:{key}:DialogportenBaseUri ('{env.DialogportenBaseUri}') is not an absolute URI.");
            }

            _byKey[key] = new DialogportenEnvironment(
                key,
                string.IsNullOrWhiteSpace(env.DisplayName) ? key.ToUpperInvariant() : env.DisplayName,
                env.DialogportenBaseUri,
                env.AfUri,
                env.MutateBaseUri,
                env.UseMaskinporten);
        }

        All = _byKey.Values.ToList();

        var defaultKey = settings.Default;
        if (string.IsNullOrWhiteSpace(defaultKey))
        {
            throw new InvalidOperationException("DialogportenEnvironments:Default must be set.");
        }
        if (!_byKey.TryGetValue(defaultKey, out var defaultEnvironment))
        {
            throw new InvalidOperationException(
                $"DialogportenEnvironments:Default is '{defaultKey}', which is not one of the configured environments ({string.Join(", ", _byKey.Keys)}).");
        }
        Default = defaultEnvironment;
    }

    public bool TryResolve(string? key, out DialogportenEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            environment = Default;
            return true;
        }
        return _byKey.TryGetValue(key, out environment!);
    }

    public DialogportenEnvironment Resolve(string? key) =>
        TryResolve(key, out var environment)
            ? environment
            : throw new ArgumentException(
                $"Unknown environment '{key}'. Configured environments: {string.Join(", ", _byKey.Keys)}.", nameof(key));
}
