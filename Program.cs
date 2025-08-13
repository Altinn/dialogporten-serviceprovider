using Altinn.ApiClients.Dialogporten;
using Digdir.BDB.Dialogporten.ServiceProvider.Auth;
using Digdir.BDB.Dialogporten.ServiceProvider.Clients;
using Digdir.BDB.Dialogporten.ServiceProvider.Components;
using Digdir.BDB.Dialogporten.ServiceProvider.Components.Account;
using Digdir.BDB.Dialogporten.ServiceProvider.Data;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddControllers();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
