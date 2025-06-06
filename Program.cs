using Altinn.ApiClients.Dialogporten;
using Digdir.BDB.Dialogporten.ServiceProvider;
using Digdir.BDB.Dialogporten.ServiceProvider.Auth;
using Digdir.BDB.Dialogporten.ServiceProvider.Clients;
using Digdir.BDB.Dialogporten.ServiceProvider.Components;
using Digdir.BDB.Dialogporten.ServiceProvider.Data;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
       .AddSimpleAuth()
       .AddDialogTokenAuthentication()
       .AddTransient<TokenGeneratorMessageHandler>()
       .AddTransient<ConsoleLoggingMessageHandler>()
       .AddSingleton<ITokenGenerator, TokenGenerator>()
       .AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>()
       .AddHostedService<EdDsaSecurityKeysCacheService>()
       .AddHostedService<QueuedHostedService>()
       .AddHostedService<RegClient>()
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
       .AddDialogportenClient(dialogportenSettings);
// .AddRefitClient<IDialogporten>(_ => new RefitSettings
// {
//     ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
//     {
//         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
//
//         PropertyNameCaseInsensitive = true,
//         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
//         Converters = {
//             new ObjectToInferredTypesConverter(),
//             new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
//         }
//     })
// })
// .ConfigureHttpClient(configuration =>
// {
//     configuration.BaseAddress = new Uri(builder.Configuration["Dialogporten:BaseUrl"]!);
// })
// .AddHttpMessageHandler<ConsoleLoggingMessageHandler>()
// .ConfigurePrimaryHttpMessageHandler<TokenGeneratorMessageHandler>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(@"DataSource=Data\myApp.db;Cache=Shared"));

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
       .AddEntityFrameworkStores<ApplicationDbContext>()
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
app.MapControllers();

await app.RunAsync();
