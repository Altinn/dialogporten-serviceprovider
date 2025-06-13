using System.Text.Json;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Services;

public class ResourceRegistryClient(IHttpClientFactory httpClientFactory, ILogger<ResourceRegistryClient> logger, IConfiguration configuration) : IHostedService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<ResourceRegistryClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public static List<MaskinportenSchemaResource> Resources => _resources;
    private static volatile List<MaskinportenSchemaResource> _resources = [];

    private readonly string _endpoint = configuration.GetValue<string>("registryUri") ?? throw new ArgumentNullException($"registryUri");

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
            var response = await httpClient.GetStringAsync(_endpoint, cancellationToken);

            var deserializedResponse = JsonSerializer.Deserialize<List<MaskinportenSchemaResource>>(response, Options);
            if (deserializedResponse != null)
            {
                deserializedResponse = deserializedResponse.OrderBy(x => x.Identifier).ToList();
                resources.AddRange(deserializedResponse);
            }
        }
        catch
        {
            _logger.LogWarning("Failed to refresh resource list from endpoint {Endpoint}", _endpoint);
        }

        _resources = resources;
    }
}
