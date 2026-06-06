using Altinn.ApiClients.Dialogporten;
using Digdir.BDB.Dialogporten.ServiceProvider.Auth;
using Digdir.BDB.Dialogporten.ServiceProvider.Clients;
using Digdir.BDB.Dialogporten.ServiceProvider.Components;
using Digdir.BDB.Dialogporten.ServiceProvider.Components.Account;
using Digdir.BDB.Dialogporten.ServiceProvider.Data;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddControllers();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    });

var serviceProviderSettings = builder.Configuration
    .GetSection("ServiceProvider")
    .Get<ServiceProviderSettings>()!;

builder.Services.TryAddSingleton<IOptions<ServiceProviderSettings>>(new OptionsWrapper<ServiceProviderSettings>(serviceProviderSettings));

var dialogportenSettings = builder.Configuration
    .GetSection("DialogportenSettings")
    .Get<DialogportenSettings>()!;

builder.Services
    .AddEndpointsApiExplorer()
    .AddSwaggerGen()
    .AddIdportenAuthentication(builder.Configuration)
    .AddBasicAuthentication()
    .AddDialogTokenAuthentication()
    .AddScoped<IdentityRedirectManager>()
    .AddTransient<TokenGeneratorMessageHandler>()
    .AddTransient<ConsoleLoggingMessageHandler>()
    .AddSingleton<ITokenGenerator, TokenGenerator>()
    .AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>()
    .AddSingleton<IPlaybookStateStore, InMemoryPlaybookStateStore>()
    .AddHostedService<EdDsaSecurityKeysCacheService>()
    .AddHostedService<QueuedHostedService>()
    .AddHostedService<ResourceRegistryClient>()
    .AddCascadingAuthenticationState()
    .AddCors(options =>
    {
        options.AddPolicy("AllowedOriginsPolicy", builder =>
        {
            // This is to ease development (ie. various locahost ports)
            // In a production setting, this should be restricted to https://af.altinn.no
            builder.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod()
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        });
    })
    .AddAuthorization(options =>
    {
        options.AddPolicy("SimpleAuth", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AuthenticationSchemes =
            [
                IdentityConstants.ApplicationScheme
            ];
        });
    })
    .AddDialogportenClient(dialogportenSettings);

builder.Services.AddSingleton<InMemoryUserStoreContext>();

builder.Services.AddIdentityCore<IdentityUser>(o =>
    {
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
    })
    .AddUserStore<InMemoryUserStore>()
    .AddSignInManager()
    .AddDefaultTokenProviders();


var app = builder.Build();

ValidateSampleFiles(app);

app.UseSwagger();
app.UseSwaggerUI();


app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowedOriginsPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapAdditionalIdentityEndpoints();
app.MapControllers();
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.AddDefaultAccount();
}
await app.RunAsync();

static void ValidateSampleFiles(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PlaybookSampleValidator");
    var samples = Directory.EnumerateFiles(app.Environment.ContentRootPath, "sample-*.json").ToList();
    if (samples.Count == 0)
    {
        return;
    }

    var totalIssues = 0;
    foreach (var path in samples)
    {
        var fileName = Path.GetFileName(path);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var issues = Digdir.BDB.Dialogporten.ServiceProvider.Playbook.PlaybookSampleValidator.Validate(doc);
            if (issues.Count == 0)
            {
                logger.LogInformation("Sample {File} OK", fileName);
                continue;
            }
            totalIssues += issues.Count;
            foreach (var issue in issues)
            {
                logger.LogWarning("[{File}] {Issue}", fileName, issue);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse/validate sample {File}", fileName);
            totalIssues++;
        }
    }
    if (totalIssues > 0)
    {
        logger.LogWarning("Validated {Count} sample file(s); found {Issues} issue(s)", samples.Count, totalIssues);
    }
}

public sealed class ServiceProviderSettings
{
    public string RegistryUri { get; set; } = null!;
    public string MutateBaseUri { get; set; } = null!;
    public DefaultAccount DefaultAccount { get; set; } = null!;

}

public sealed class DefaultAccount
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}
