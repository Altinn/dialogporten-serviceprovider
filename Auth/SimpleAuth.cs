using Microsoft.AspNetCore.Identity;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Auth;

public static class SimpleAuth
{
    public static IServiceCollection AddSimpleAuth(this IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
                {
                    options.LoginPath = "/account/login";
                    options.LogoutPath = "/account/logout";
                    options.AccessDeniedPath = "/error";
                }
            );
        // .AddCookie("Identity.External", options =>
        // {
        //     options.LoginPath = "/account/login";
        //     options.LogoutPath = "/account/logout";
        //     options.AccessDeniedPath = "/error";
        // }


        return services;
    }
}
