namespace Digdir.BDB.Dialogporten.ServiceProvider.Auth;

public static class SimpleAuth
{
    public static IServiceCollection AddSimpleAuth(this IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddCookie("simpleLogin", options =>
                {
                    options.LoginPath = "/login";
                    options.LogoutPath = "/logout";
                    options.AccessDeniedPath = "/error";
                }
            );

        return services;
    }
}
