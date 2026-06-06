using System.Text.Json.Nodes;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public sealed record PlaybookBlueprint(
    Guid DialogId,
    JsonArray Patches,
    IReadOnlyDictionary<string, FceContent> FceContents);

public sealed record FceContent(string MediaType, string Content);
