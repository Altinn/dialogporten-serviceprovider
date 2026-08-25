using System.Text.Json.Nodes;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public sealed record PlaybookBlueprint(
    Guid DialogId,
    JsonArray Patches,
    IReadOnlyDictionary<string, FceContent> FceContents,
    IReadOnlyDictionary<string, object?> InitialVars,
    IReadOnlyList<StageBehavior> StageBehaviors,
    IReadOnlyDictionary<string, int> StageCursors)
{
    /// <summary>
    /// Key of the environment (see <c>DialogportenEnvironments</c> in configuration) this playbook's
    /// dialog lives in. Set when the dialog is created; every later mutation must target the same
    /// environment. Null/empty means the configured default environment.
    /// </summary>
    public string? EnvironmentKey { get; init; }

    /// <summary>
    /// Base URI the playbook's GUI action and FCE URLs were built from, kept so later stages emit
    /// the same URLs the dialog already carries. Null/empty means "resolve it again".
    /// </summary>
    public string? MutateBaseUri { get; init; }

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
