using Digdir.BDB.Dialogporten.ServiceProvider.Data;
using Microsoft.AspNetCore.Identity;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Components.Account;

internal static class DefaultAccount
{
    public static async Task AddDefaultAccount(this IServiceProvider services)
    {
        var userStore = services.GetRequiredService<IUserStore<ApplicationUser>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILogger<ApplicationUser>>();

        var defaultUsername = configuration.GetValue<string>("ServiceProvider:DefaultAccount:Username");
        var defaultPassword = configuration.GetValue<string>("ServiceProvider:DefaultAccount:Password");

        if (defaultUsername == null || defaultPassword == null)
        {
            logger.LogWarning("no default account configuration found");
            return;
        }

        var admin = await userManager.FindByNameAsync(defaultPassword);
        logger.LogInformation("Creating default account");
        if (admin == null)
        {
            var user = new ApplicationUser();
            await userStore.SetUserNameAsync(user, defaultUsername, CancellationToken.None);
            var result = await userManager.CreateAsync(user, defaultPassword);
            if (!result.Succeeded)
            {
                logger.LogInformation("Default account already created!");
            }
            else
            {
                logger.LogInformation("Created default account");
            }
        }
    }

}
