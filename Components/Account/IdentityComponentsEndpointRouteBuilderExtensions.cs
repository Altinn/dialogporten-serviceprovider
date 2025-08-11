using System.Security.Claims;
using Digdir.BDB.Dialogporten.ServiceProvider.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;


namespace Digdir.BDB.Dialogporten.ServiceProvider.Components.Account;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
    [AllowAnonymous]
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");


        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            signInManager.AuthenticationScheme = IdentityConstants.ApplicationScheme;
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        accountGroup.MapGet("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager) =>
        {
            signInManager.AuthenticationScheme = IdentityConstants.ApplicationScheme;
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/");
        });
        return accountGroup;
    }
}
