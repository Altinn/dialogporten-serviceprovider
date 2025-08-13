using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Data;

public class InMemoryUserStoreContext
{
    private readonly ConcurrentDictionary<string, ApplicationUser> _users = new();
    private readonly ConcurrentDictionary<string, string> _passwords = new();

    public bool TryAddUser(ApplicationUser user)
    {
        user.NormalizedUserName = user.UserName!.ToUpperInvariant();
        return _users.TryAdd(user.Id, user);
    }

    public bool TryUpdateUser(string userId, ApplicationUser user)
    {
        return _users.TryUpdate(userId, user, user);
    }
    public bool TryRemoveUser(string userId, out ApplicationUser? user)
    {
        return _users.TryRemove(userId, out user);
    }
    public bool TryRemovePassword(string userId, out string? password)
    {
        return _passwords.TryRemove(userId, out password);
    }
    public ApplicationUser? FindUserById(string userId)
    {
        _users.TryGetValue(userId, out var user);
        return user;
    }
    public ApplicationUser? FindUserByName(string name)
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
