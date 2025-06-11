using System.Text.Json;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public class RegClient(IHttpClientFactory httpClientFactory, ILogger<RegClient> logger) : IHostedService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<RegClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public static List<MaskinportenSchemaResource> Resources => _resources;
    private static volatile List<MaskinportenSchemaResource> _resources = [];

    // Amund: en eller annen form for config
    private const string Endpoint = "https://platform.tt02.altinn.no/resourceregistry/api/v1/resource/resourcelist";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshResourceListAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task RefreshResourceListAsync(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var resources = new List<MaskinportenSchemaResource>();

        try
        {
            var response = await httpClient.GetStringAsync(Endpoint, cancellationToken);

            var deserializedResponse = JsonSerializer.Deserialize<List<MaskinportenSchemaResource>>(response, Options);
            if (deserializedResponse != null)
            {
                deserializedResponse = deserializedResponse.OrderBy(x => x.Identifier).ToList();
                resources.AddRange(deserializedResponse);
            }
        }
        catch
        {
            _logger.LogWarning($"Failed to refresh resource list from endpoint {Endpoint}");
        }

        _resources = resources;
    }
}
