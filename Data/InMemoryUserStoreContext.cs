using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Data;

public sealed class InMemoryUserStoreContext
{
    private readonly ConcurrentDictionary<string, IdentityUser> _users = new();
    private readonly ConcurrentDictionary<string, string> _passwords = new();

    public bool TryAddUser(IdentityUser user)
    {
        user.NormalizedUserName = user.UserName!.ToUpperInvariant();
        return _users.TryAdd(user.Id, user);
    }

    public bool TryUpdateUser(string userId, IdentityUser user)
    {
        return _users.TryUpdate(userId, user, user);
    }
    public bool TryRemoveUser(string userId, out IdentityUser? user)
    {
        return _users.TryRemove(userId, out user);
    }
    public bool TryRemovePassword(string userId, out string? password)
    {
        return _passwords.TryRemove(userId, out password);
    }
    public IdentityUser? FindUserById(string userId)
    {
        _users.TryGetValue(userId, out var user);
        return user;
    }
    public IdentityUser? FindUserByName(string name)
    {
        return _users.Values.FirstOrDefault(u => u.NormalizedUserName == name);
    }

    public void SetPasswordHash(string userId, string passwordHash)
    {
        _passwords[userId] = passwordHash;
    }

    public bool TryGetPasswordHash(string userId, [NotNullWhen(true)] out string? passwordHash)
    {
        return _passwords.TryGetValue(userId, out passwordHash);
    }

    public bool HasPassword(string userId)
    {
        return _passwords.ContainsKey(userId);
    }
}
