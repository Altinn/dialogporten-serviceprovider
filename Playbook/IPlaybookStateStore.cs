namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public interface IPlaybookStateStore
{
    Task<string> CreateAsync(PlaybookBlueprint blueprint, CancellationToken cancellationToken);
    Task<PlaybookBlueprint?> GetAsync(string id, CancellationToken cancellationToken);

    // Phase B: per-session variable state. CreateAsync seeds these from blueprint.InitialVars.
    Task<IReadOnlyDictionary<string, object?>> GetSessionVarsAsync(string id, CancellationToken cancellationToken);
    Task UpdateSessionVarsAsync(string id, IReadOnlyDictionary<string, object?> vars, CancellationToken cancellationToken);
}
