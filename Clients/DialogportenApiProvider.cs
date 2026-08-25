using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Refit;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Clients;

/// <summary>
/// Hands out an <see cref="IServiceownerApi"/> bound to a specific environment.
/// <para>
/// <c>AddDialogportenClient</c> can only register a single <see cref="IServiceownerApi"/> with one
/// hardcoded base address, so the playbook flows go through this provider instead: one named
/// <see cref="HttpClient"/> per configured environment (registered in <c>Program.cs</c>, with the
/// same Maskinporten handler the SDK uses), wrapped in a Refit client on demand.
/// </para>
/// </summary>
public interface IDialogportenApiProvider
{
    IServiceownerApi GetApi(DialogportenEnvironment environment);
    IServiceownerApi GetApi(string? environmentKey);
}

public sealed class DialogportenApiProvider(
    IHttpClientFactory httpClientFactory,
    IDialogportenEnvironmentRegistry registry) : IDialogportenApiProvider
{
    // Mirrors the serializer settings Altinn.ApiClients.Dialogporten uses for its own clients.
    private static readonly RefitSettings RefitSettings = new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        })
    };

    // Building the request builder is the expensive part (reflection over the interface), so it is
    // shared; the HttpClient itself is taken fresh from the factory on every call.
    private static readonly Lazy<IRequestBuilder<IServiceownerApi>> RequestBuilderCache =
        new(() => RequestBuilder.ForType<IServiceownerApi>(RefitSettings));

    public IServiceownerApi GetApi(DialogportenEnvironment environment)
    {
        var httpClient = httpClientFactory.CreateClient(environment.HttpClientName);
        return RestService.For(httpClient, RequestBuilderCache.Value);
    }

    public IServiceownerApi GetApi(string? environmentKey) => GetApi(registry.Resolve(environmentKey));
}
