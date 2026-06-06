using System.Text.Json.Nodes;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public sealed record PlaybookBlueprint(
    Guid DialogId,
    JsonArray Patches,
    IReadOnlyDictionary<string, FceContent> FceContents,
    IReadOnlyDictionary<string, object?> InitialVars,
    IReadOnlyList<StageBehavior> StageBehaviors)
{
    // Phase A back-compat constructor — no vars, no per-stage behavior metadata.
    public PlaybookBlueprint(Guid dialogId, JsonArray patches, IReadOnlyDictionary<string, FceContent> fceContents)
        : this(
            dialogId,
            patches,
            fceContents,
            new Dictionary<string, object?>(),
            Enumerable.Range(0, patches.Count).Select(_ => new StageBehavior([], [])).ToList())
    { }
}

public sealed record FceContent(string MediaType, string Content);

public sealed record StageBehavior(
    IReadOnlyList<Stmt> Effects,
    IReadOnlyList<Expr?> ActionWhens);
