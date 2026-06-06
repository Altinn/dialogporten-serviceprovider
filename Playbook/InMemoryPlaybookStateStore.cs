using System.Collections.Concurrent;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public sealed class InMemoryPlaybookStateStore : IPlaybookStateStore
{
    private readonly ConcurrentDictionary<string, PlaybookBlueprint> _store = new();

    public Task<string> CreateAsync(PlaybookBlueprint blueprint, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        _store[id] = blueprint;
        return Task.FromResult(id);
    }

    public Task<PlaybookBlueprint?> GetAsync(string id, CancellationToken cancellationToken)
    {
        _store.TryGetValue(id, out var blueprint);
        return Task.FromResult(blueprint);
    }
}
