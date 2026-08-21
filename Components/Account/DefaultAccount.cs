using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Components.Account;

internal static class DefaultAccount
{
    public static async Task AddDefaultAccount(this IServiceProvider services)
    {
        var userStore = services.GetRequiredService<IUserStore<IdentityUser>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var options = services.GetRequiredService<IOptions<ServiceProviderSettings>>();
        var logger = services.GetRequiredService<ILogger<IdentityUser>>();

        var defaultUsername = options.Value.DefaultAccount.Username;
        var defaultPassword = options.Value.DefaultAccount.Password;

        if (string.IsNullOrEmpty(defaultPassword) || string.IsNullOrEmpty(defaultUsername))
        {
            logger.LogWarning("no default account configuration found");
            return;
        }

        logger.LogInformation("Creating default account");
        var user = new IdentityUser();
        await userStore.SetUserNameAsync(user, defaultUsername, CancellationToken.None);
        var result = await userManager.CreateAsync(user, defaultPassword);
        if (result.Succeeded)
            logger.LogInformation("Created default account");
        else
            logger.LogInformation("Something went wrong creating default account");
    }

}
