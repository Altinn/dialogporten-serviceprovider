namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public interface IPlaybookStateStore
{
    Task<string> CreateAsync(PlaybookBlueprint blueprint, CancellationToken cancellationToken);
    Task<PlaybookBlueprint?> GetAsync(string id, CancellationToken cancellationToken);
}
