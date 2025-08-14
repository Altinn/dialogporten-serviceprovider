using Microsoft.AspNetCore.Identity;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Data;

public sealed class InMemoryUserStore(InMemoryUserStoreContext userStore) :
    IUserPasswordStore<IdentityUser>
{

    public Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = user.UserName?.ToUpperInvariant();
        

        return Task.FromResult(userStore.TryAddUser(user) ? IdentityResult.Success : IdentityResult.Failed());
    }

    public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(userStore.TryUpdateUser(user.Id, user) ? IdentityResult.Success : IdentityResult.Failed());
    }

    public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        userStore.TryRemoveUser(user.Id, out _);
        userStore.TryRemovePassword(user.Id, out _);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(IdentityUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;

    }
    public Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = userStore.FindUserById(userId);
        return Task.FromResult(user);
    }

    public Task<IdentityUser?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        var user = userStore.FindUserByName(name);
        return Task.FromResult(user);
    }

    public Task SetPasswordHashAsync(IdentityUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        if (passwordHash != null)
        {
            userStore.SetPasswordHash(user.Id, passwordHash);
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        userStore.TryGetPasswordHash(user.Id, out var passwordHash);
        return Task.FromResult(passwordHash);
    }

    public Task<bool> HasPasswordAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(userStore.HasPassword(user.Id));

    public void Dispose() { }
}
