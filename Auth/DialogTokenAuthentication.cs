using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.IdentityModel.Tokens;
using ScottBrady.IdentityModel;
using ScottBrady.IdentityModel.Tokens;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Auth;

public static class DialogTokenServiceCollectionExtension
{
    public static IServiceCollection AddDialogTokenAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddJwtBearer("DialogToken", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = false,
                    ValidateIssuer = false,
                    // ConfigurationManager does not support EdDsa, so we need to roll our own refresh/cache of JWKS
                    // in the service below. Because IssuerSigningKeyResolver does not have an async counterpart, we
                    // need to proxy the keys via a static field.
                    IssuerSigningKeyResolver = (_, _, _, _) => EdDsaSecurityKeysCacheService.EdDsaSecurityKeys
                };
                options.RequireHttpsMetadata = true;
            });

        return services;
    }
}

public class EdDsaSecurityKeysCacheService : IHostedService, IDisposable
{
    public static List<EdDsaSecurityKey> EdDsaSecurityKeys => _keys;
    private static volatile List<EdDsaSecurityKey> _keys = new();

    private PeriodicTimer? _timer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EdDsaSecurityKeysCacheService> _logger;

    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(12);

    // We accept dialog tokens from every configured environment, since a playbook may be created in
    // any of them (see DialogportenEnvironments in configuration). Usually one would only allow a
    // single environment (issuer) here. Endpoints that cannot be reached are logged and skipped, so
    // eg. a configured-but-not-running local Dialogporten is harmless.
    private readonly List<string> _wellKnownEndpoints;

    public EdDsaSecurityKeysCacheService(
        IHttpClientFactory httpClientFactory,
        IDialogportenEnvironmentRegistry environments,
        ILogger<EdDsaSecurityKeysCacheService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _wellKnownEndpoints = environments.All
            .Select(x => x.JwksUri)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            _timer = new PeriodicTimer(_refreshInterval);
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await RefreshAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while refreshing the EdDsa keys.");
                }
            }
        }, cancellationToken);

        await RefreshAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var keys = new List<EdDsaSecurityKey>();

        foreach (var endpoint in _wellKnownEndpoints)
        {
            try
            {
                var response = await httpClient.GetStringAsync(endpoint, cancellationToken);
                var jwks = new JsonWebKeySet(response);
                foreach (var jwk in jwks.Keys)
                {
                    if (ExtendedJsonWebKeyConverter.TryConvertToEdDsaSecurityKey(jwk, out var edDsaKey))
                    {
                        keys.Add(edDsaKey);
                    }
                }
            }
            catch (Exception ex)
            {
                // A configured environment may simply not be running (typically local Dialogporten),
                // so keep this to a single line and carry on with the endpoints that do answer.
                _logger.LogWarning("Failed to retrieve keys from {endpoint}: {error}", endpoint, ex.Message);
            }
        }

        _logger.LogInformation("Refreshed EdDsa keys cache with {count} keys", keys.Count);

        var newKeys = keys.ToList();
        _keys = newKeys; // Atomic replace
    }
}
