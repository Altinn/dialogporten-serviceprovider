using System.Collections.Concurrent;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public sealed class InMemoryPlaybookStateStore : IPlaybookStateStore
{
    private readonly ConcurrentDictionary<string, PlaybookBlueprint> _blueprints = new();
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, object?>> _sessionVars = new();

    public Task<string> CreateAsync(PlaybookBlueprint blueprint, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        _blueprints[id] = blueprint;
        _sessionVars[id] = new Dictionary<string, object?>(blueprint.InitialVars);
        return Task.FromResult(id);
    }

    public Task<PlaybookBlueprint?> GetAsync(string id, CancellationToken cancellationToken)
    {
        _blueprints.TryGetValue(id, out var blueprint);
        return Task.FromResult(blueprint);
    }

    public Task<IReadOnlyDictionary<string, object?>> GetSessionVarsAsync(string id, CancellationToken cancellationToken)
    {
        _sessionVars.TryGetValue(id, out var vars);
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(
            vars ?? new Dictionary<string, object?>());
    }

    public Task UpdateSessionVarsAsync(string id, IReadOnlyDictionary<string, object?> vars, CancellationToken cancellationToken)
    {
        _sessionVars[id] = vars;
        return Task.CompletedTask;
    }
}
