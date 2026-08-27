// playbook-lint — compile a .playbook.yaml with the server's own DslCompiler and report
// compile errors plus a set of authoring warnings that only show up at runtime otherwise.
//
//   dotnet run --project .claude/skills/playbook-author/lint -- path/to/x.playbook.yaml
//
// Exit codes: 0 = all files compiled (warnings may still be printed), 1 = at least one file
// failed to compile, 2 = bad usage.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: playbook-lint <file.playbook.yaml> [more files...]");
    return 2;
}

var failed = 0;
foreach (var path in args)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"FAIL {path}: file not found");
        failed++;
        continue;
    }

    DslCompileResult result;
    try
    {
        result = DslCompiler.Compile(File.ReadAllText(path));
    }
    catch (DslCompilationException ex)
    {
        Console.WriteLine($"FAIL {path}");
        Console.WriteLine($"     compile error: {ex.Message}");
        failed++;
        continue;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {path}");
        Console.WriteLine($"     {ex.GetType().Name}: {ex.Message}");
        failed++;
        continue;
    }

    var warnings = Lint.Run(result);

    var startName = Lint.NameOf(result, result.InitialCursor);
    Console.WriteLine(
        $"OK   {path}  ({result.Blueprint.Patches.Count} stages, " +
        $"{result.Blueprint.InitialVars.Count} vars, {result.Blueprint.FceContents.Count} fce, " +
        $"start='{startName}')");

    foreach (var w in warnings)
    {
        Console.WriteLine($"     warn: {w}");
    }
}

return failed == 0 ? 0 : 1;

internal static class Lint
{
    private static readonly Regex GotoRe = new(@"\$goto=(\d+)", RegexOptions.Compiled);
    private static readonly Regex RandomRe = new(@"\$random=([\d:|]+)", RegexOptions.Compiled);
    private static readonly Regex GotoVarRe = new(@"\$gotovar=", RegexOptions.Compiled);
    private static readonly Regex NextRe = new(@"""\$next""", RegexOptions.Compiled);
    private static readonly Regex PrevRe = new(@"""\$previous""", RegexOptions.Compiled);
    private static readonly Regex FceRefRe = new(@"/fce/named/\{stateId\}/([^""?]+)", RegexOptions.Compiled);

    public static string NameOf(DslCompileResult r, int cursor) =>
        r.Blueprint.StageCursors.FirstOrDefault(kv => kv.Value == cursor).Key ?? $"#{cursor}";

    public static List<string> Run(DslCompileResult r)
    {
        var w = new List<string>();
        var bp = r.Blueprint;
        var stageCount = bp.Patches.Count;

        // Reuse the repo's own JSON-level sample validator: extendedStatus length, activity
        // type/description pairing, guiAction verb/method, $goto range, FCE reference hygiene.
        w.AddRange(RunSampleValidator(r));

        var dynamicTargets = false;
        var edges = new List<HashSet<int>>();

        for (var i = 0; i < stageCount; i++)
        {
            var name = NameOf(r, i);
            var ops = bp.Patches[i] as JsonArray ?? [];
            var raw = ops.ToJsonString();
            var behavior = i < bp.StageBehaviors.Count ? bp.StageBehaviors[i] : StageBehavior.Empty;

            var hasUnconditionalGoto = behavior.Gotos.Any(g => g.When is null);
            var hasGuiActions = ops.Any(o => PathOf(o) == "/guiActions");

            // A stage that emits no patch operations makes MutateController return 400 — unless
            // it is a pure router that always dispatches somewhere else before rendering.
            if (ops.Count == 0 && !hasUnconditionalGoto)
            {
                w.Add(behavior.Gotos.Count > 0
                    ? $"stage '{name}': router with only conditional goto rules and no content — " +
                      "if no rule matches it falls through and renders nothing (runtime 400). Add a final unconditional target."
                    : $"stage '{name}': emits no patch operations — rendering it fails at runtime (400).");
            }

            // No way forward: the dialog keeps whatever guiActions the previous stage left behind.
            if (!hasGuiActions && behavior.Gotos.Count == 0 && ops.Count > 0)
            {
                w.Add($"stage '{name}': no actions and no goto — the previous stage's buttons stay on screen (dead end).");
            }

            // Every action guarded and no `when: else` fallback: the stage can render with zero buttons.
            if (behavior.ActionWhens.Count > 0
                && behavior.ActionWhens.All(x => x is not null)
                && behavior.ActionWhens.All(x => x is not ElseLit))
            {
                w.Add($"stage '{name}': every action has a 'when' and none is 'when: else' — " +
                      "the stage renders with no buttons unless the conditions are exhaustive. Verify, or add a 'when: else' escape hatch.");
            }

            w.AddRange(CheckPriorities(name, ops));

            // Outgoing edges for reachability.
            var outs = new HashSet<int>();
            foreach (var g in behavior.Gotos)
            {
                if (g.Cursor is { } c) outs.Add(c);
                else dynamicTargets = true;
            }
            foreach (Match m in GotoRe.Matches(raw)) outs.Add(int.Parse(m.Groups[1].Value));
            foreach (Match m in RandomRe.Matches(raw))
            {
                foreach (var part in m.Groups[1].Value.Split('|'))
                {
                    if (int.TryParse(part.Split(':')[0], out var c)) outs.Add(c);
                }
            }
            if (NextRe.IsMatch(raw)) outs.Add(i + 1);
            if (PrevRe.IsMatch(raw)) outs.Add(i - 1);

            // An HTML embed on this stage can carry a form posting to {formAction:STAGE}, which is
            // a real edge out of the stage even though no guiAction points there.
            foreach (Match m in FceRefRe.Matches(raw))
            {
                if (!bp.FceContents.TryGetValue(m.Groups[1].Value, out var fce)) continue;
                foreach (var target in EmbedPlaceholders.ReferencedStages(fce.Content))
                {
                    if (bp.StageCursors.TryGetValue(target, out var targetCursor)) outs.Add(targetCursor);
                }
            }
            if (GotoVarRe.IsMatch(raw)) dynamicTargets = true;

            foreach (var t in outs.Where(t => t < 0 || t >= stageCount).ToList())
            {
                w.Add($"stage '{name}': target resolves to out-of-range cursor {t} " +
                      "(a 'next'/'previous' target at the edge of the stage list).");
            }

            edges.Add(outs);
        }

        // Reachability from the start stage.
        var seen = new HashSet<int> { r.InitialCursor };
        var queue = new Queue<int>([r.InitialCursor]);
        while (queue.Count > 0)
        {
            foreach (var next in edges[queue.Dequeue()].Where(n => n >= 0 && n < stageCount))
            {
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }

        var unreachable = Enumerable.Range(0, stageCount).Where(i => !seen.Contains(i)).ToList();
        if (unreachable.Count > 0)
        {
            var suffix = dynamicTargets ? " (approximate: @var(...) targets are not traced)" : "";
            w.Add($"unreachable from start: {string.Join(", ", unreachable.Select(i => $"'{NameOf(r, i)}'"))}{suffix}");
        }

        return w;
    }

    private static IEnumerable<string> CheckPriorities(string stageName, JsonArray ops)
    {
        foreach (var op in ops)
        {
            if (PathOf(op) != "/guiActions") continue;
            if (op?["value"] is not JsonArray actions) continue;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in actions)
            {
                var p = a?["priority"]?.GetValue<string>() ?? "";
                counts[p] = counts.GetValueOrDefault(p) + 1;
            }
            // Dialogporten: max 1 primary, 1 secondary, 5 tertiary — so max 7 actions per stage.
            // The cap applies to authored actions; `when:` filtering happens after the PATCH is built.
            foreach (var (priority, max) in new[] { ("primary", 1), ("secondary", 1), ("tertiary", 5) })
            {
                var n = counts.GetValueOrDefault(priority);
                if (n > max)
                {
                    yield return $"stage '{stageName}': {n} actions have priority '{priority}' — " +
                                 $"Dialogporten allows at most {max}, and rejects the whole PATCH otherwise.";
                }
            }
            if (actions.Count > 7)
            {
                yield return $"stage '{stageName}': {actions.Count} actions — Dialogporten caps a dialog at 7 " +
                             "(1 primary + 1 secondary + 5 tertiary), counted before 'when' filtering.";
            }
        }
    }

    private static string? PathOf(JsonNode? op) => op?["path"]?.GetValue<string>();

    /// <summary>
    /// Re-shapes the compiled blueprint into the JSON envelope PlaybookSampleValidator expects
    /// (the /playbook/create request body) so the DSL path gets the same checks the JSON path has.
    /// </summary>
    private static IEnumerable<string> RunSampleValidator(DslCompileResult r)
    {
        var fce = new JsonObject();
        foreach (var (name, content) in r.Blueprint.FceContents)
        {
            fce[name] = new JsonObject
            {
                ["mediaType"] = content.MediaType,
                ["content"] = content.Content
            };
        }

        var envelope = new JsonObject
        {
            ["playbookState"] = new JsonObject
            {
                ["Patches"] = r.Blueprint.Patches.DeepClone(),
                ["Cursor"] = r.InitialCursor
            },
            ["fceContents"] = fce
        };

        using var doc = JsonDocument.Parse(envelope.ToJsonString());

        // The validator reports stage indices; rewrite them to DSL stage names.
        var stageIndexRe = new Regex(@"^stage (\d+):", RegexOptions.Compiled);
        return PlaybookSampleValidator.Validate(doc)
            .Select(issue => stageIndexRe.Replace(issue, m => $"stage '{NameOf(r, int.Parse(m.Groups[1].Value))}':"))
            .ToList();
    }
}
