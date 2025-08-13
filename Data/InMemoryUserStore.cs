using Microsoft.AspNetCore.Identity;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Data;

public class InMemoryUserStore(InMemoryUserStoreContext userStore) :
    IUserPasswordStore<ApplicationUser>
{

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = user.UserName?.ToUpperInvariant();
        

        return Task.FromResult(userStore.TryAddUser(user) ? IdentityResult.Success : IdentityResult.Failed());
    }

    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(userStore.TryUpdateUser(user.Id, user) ? IdentityResult.Success : IdentityResult.Failed());
    }

    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        userStore.TryRemoveUser(user.Id, out _);
        userStore.TryRemovePassword(user.Id, out _);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedName;
        return Task.CompletedTask;

    }
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = userStore.FindUserById(userId);
        return Task.FromResult(user);
    }

    public Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        var user = userStore.FindUserByName(name);
        return Task.FromResult(user);
    }

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        if (passwordHash != null)
        {
            userStore.SetPasswordHash(user.Id, passwordHash);
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        userStore.TryGetPasswordHash(user.Id, out var passwordHash);
        return Task.FromResult(passwordHash);
    }

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(userStore.HasPassword(user.Id));

    public void Dispose() { }
}
