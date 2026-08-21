using System.Text.Json.Nodes;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public sealed record PlaybookBlueprint(
    Guid DialogId,
    JsonArray Patches,
    IReadOnlyDictionary<string, FceContent> FceContents,
    IReadOnlyDictionary<string, object?> InitialVars,
    IReadOnlyList<StageBehavior> StageBehaviors,
    IReadOnlyDictionary<string, int> StageCursors)
{
    // Phase A back-compat constructor — no vars, no per-stage behavior metadata, no stage names.
    public PlaybookBlueprint(Guid dialogId, JsonArray patches, IReadOnlyDictionary<string, FceContent> fceContents)
        : this(
            dialogId,
            patches,
            fceContents,
            new Dictionary<string, object?>(),
            Enumerable.Range(0, patches.Count).Select(_ => StageBehavior.Empty).ToList(),
            new Dictionary<string, int>())
    { }
}

public sealed record FceContent(string MediaType, string Content);

public sealed record StageBehavior(
    IReadOnlyList<Stmt> Effects,
    IReadOnlyList<Expr?> ActionWhens,
    IReadOnlyList<GotoRule> Gotos)
{
    public static readonly StageBehavior Empty = new([], [], []);
}

/// <summary>
/// One rule of a router stage's <c>goto:</c> list. <see cref="When"/> null means unconditional.
/// Exactly one of <see cref="Cursor"/> (fixed stage, resolved at compile time) or
/// <see cref="Var"/> (session var holding a stage name, resolved at dispatch time) is set.
/// </summary>
public sealed record GotoRule(Expr? When, int? Cursor, string? Var);
