namespace Digdir.BDB.Dialogporten.ServiceProvider.Auth;

public static class SimpleAuth
{
    public static IServiceCollection AddSimpleAuth(this IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddCookie("Identity.Application", options =>
                {
                    options.LoginPath = "/login";
                    options.LogoutPath = "/logout";
                    options.AccessDeniedPath = "/error";
                }
            )
            .AddCookie("Identity.External", options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "account/logout";
                options.AccessDeniedPath = "/error";
            }
        );

        return services;
    }
}
