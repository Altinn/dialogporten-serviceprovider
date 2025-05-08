using System.Text.Json;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public class RegClient : IHostedService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RegClient> _logger;

    public static List<MaskinportenSchemaResource> Resources => _resources;
    private static volatile List<MaskinportenSchemaResource> _resources = new();

    // Amund: en eller annen form for config
    private const string Endpoint = "https://platform.tt02.altinn.no/resourceregistry/api/v1/resource/resourcelist";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RegClient(IHttpClientFactory httpClientFactory, ILogger<RegClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
            // Amund: Proper log kanskje?
            _logger.LogWarning("Something went wrong");
        }

        _resources = resources;
    }
}
