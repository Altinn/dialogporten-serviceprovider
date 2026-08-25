using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Clients;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[Authorize(AuthenticationSchemes = "DialogToken")]
[ApiController]
[Route("mutate")]
[EnableCors("AllowedOriginsPolicy")]
public class MutateController(
    IDialogportenApiProvider apiProvider,
    IPlaybookStateStore stateStore,
    IOptions<ServiceProviderSettings> options,
    ILogger<MutateController> logger) : ControllerBase
{
    [HttpPost]
    [Route("{stateId}/{cursor:int}")]
    public async Task<IActionResult> MutatePlaybook(
        [FromRoute] string stateId,
        [FromRoute] int cursor,
        CancellationToken cancellationToken)
    {
        var blueprint = await stateStore.GetAsync(stateId, cancellationToken);
        if (blueprint is null)
        {
            return NotFound();
        }

        var tokenDialogIdRaw = User.FindFirst("i")?.Value;
        if (!Guid.TryParse(tokenDialogIdRaw, out var tokenDialogId) || tokenDialogId != blueprint.DialogId)
        {
            return Forbid();
        }

        if (cursor < 0 || cursor >= blueprint.Patches.Count)
        {
            return NotFound();
        }

        // The dialog lives in the environment it was created in, so every mutation must be sent there.
        IServiceownerApi dialogporten;
        try
        {
            dialogporten = apiProvider.GetApi(blueprint.EnvironmentKey);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(
                "Playbook stateId={StateId} references unknown environment '{Environment}': {Error}",
                stateId, blueprint.EnvironmentKey, ex.Message);
            return BadRequest(ex.Message);
        }

        // Phase B: apply this stage's effects to session vars, then persist.
        var sessionVars = await stateStore.GetSessionVarsAsync(stateId, cancellationToken);
        logger.LogInformation(
            "[mutate] stateId={StateId} env={Environment} cursor={Cursor} entry-vars={Vars} blueprint-has-{NumBehaviors}-stage-behaviors",
            stateId, blueprint.EnvironmentKey ?? "(default)", cursor, FormatVars(sessionVars), blueprint.StageBehaviors.Count);

        // Phase C: per-render RNG. If session var _seed is set, derive a deterministic seed
        // from "<_seed>|<stateId>" via MD5 so two playbooks with the same _seed but different
        // stateIds diverge, while the same playbook replays identically.
        var rng = CreateRng(sessionVars, stateId);

        // Phase E: effect + router loop. Apply the entered stage's effects; if the stage has
        // goto rules, dispatch to the first matching target and repeat there. The stage the
        // loop settles on is the one that renders.
        const int maxGotoHops = 16;
        var hops = 0;
        var anyEffectsApplied = false;
        while (true)
        {
            if (cursor >= blueprint.StageBehaviors.Count)
            {
                logger.LogInformation(
                    "[mutate]   NO stage behaviors at cursor={Cursor} (blueprint StageBehaviors.Count={Count}); Phase B inactive for this playbook",
                    cursor, blueprint.StageBehaviors.Count);
                break;
            }

            var stageBehavior = blueprint.StageBehaviors[cursor];
            logger.LogInformation(
                "[mutate]   stage[{Cursor}] effects={EffectCount} action-whens={WhenCount} gotos={GotoCount}",
                cursor, stageBehavior.Effects.Count, stageBehavior.ActionWhens.Count(w => w != null), stageBehavior.Gotos.Count);

            foreach (var stmt in stageBehavior.Effects)
            {
                try
                {
                    var before = sessionVars;
                    sessionVars = Evaluator.Apply(stmt, sessionVars, rng);
                    logger.LogInformation(
                        "[mutate]   effect {Stmt} applied: {Before} → {After}",
                        FormatStmt(stmt), FormatVars(before), FormatVars(sessionVars));
                }
                catch (ScriptException ex)
                {
                    logger.LogWarning("Effect failed for stateId={StateId} cursor={Cursor}: {Error}", stateId, cursor, ex.Message);
                    return BadRequest($"Effect failed: {ex.Message}");
                }
            }
            anyEffectsApplied |= stageBehavior.Effects.Count > 0;

            if (stageBehavior.Gotos.Count == 0)
            {
                break;
            }

            var next = ResolveGotoRules(stageBehavior.Gotos, sessionVars, blueprint.StageCursors, cursor, out var gotoError);
            if (gotoError is not null)
            {
                logger.LogWarning("Goto dispatch failed for stateId={StateId} cursor={Cursor}: {Error}", stateId, cursor, gotoError);
                return BadRequest($"Goto dispatch failed: {gotoError}");
            }
            if (next is null)
            {
                break; // no rule matched — fall through and render this stage
            }
            if (++hops > maxGotoHops)
            {
                logger.LogWarning("Goto chain exceeded {Max} hops for stateId={StateId} (possible cycle)", maxGotoHops, stateId);
                return BadRequest($"Goto chain exceeded {maxGotoHops} hops (possible cycle)");
            }
            logger.LogInformation("[mutate]   goto dispatch: stage[{From}] → stage[{To}]", cursor, next.Value);
            cursor = next.Value;
            if (cursor < 0 || cursor >= blueprint.Patches.Count)
            {
                return BadRequest($"Goto dispatched to out-of-range cursor {cursor}");
            }
        }

        if (anyEffectsApplied)
        {
            await stateStore.UpdateSessionVarsAsync(stateId, sessionVars, cancellationToken);
        }

        var compiler = new PlaybookCompiler(options.Value)
        {
            Progress = 0,
            Rng = rng,
            SessionVars = sessionVars,
            StageCursors = blueprint.StageCursors
        };

        var dialogResponse = await dialogporten.V1ServiceOwnerDialogsQueriesGetDialog(blueprint.DialogId, null!, cancellationToken);
        if (!dialogResponse.IsSuccessful)
        {
            logger.LogWarning(
                "Dialogporten GET /dialogs/{DialogId} returned {StatusCode}. Body: {Body}",
                blueprint.DialogId, (int)dialogResponse.StatusCode, dialogResponse.Error?.Content);
            return BadRequest();
        }

        compiler.Progress = dialogResponse.Content!.Progress ?? 0;

        List<JsonPatchOperations_Operation> patches;
        try
        {
            patches = await compiler.CompilePatches(stateId, blueprint, cursor);
        }
        catch (InvalidOperationException ex)
        {
            // $gotovar resolution failure — var missing, not a string, or not a stage name.
            logger.LogWarning("Patch compile failed for stateId={StateId} cursor={Cursor}: {Error}", stateId, cursor, ex.Message);
            return BadRequest(ex.Message);
        }
        if (patches.Count == 0)
        {
            return BadRequest();
        }

        // Phase B: filter /guiActions by per-action when expressions using current session vars.
        if (cursor < blueprint.StageBehaviors.Count)
        {
            FilterActionsByWhen(patches, blueprint.StageBehaviors[cursor].ActionWhens, sessionVars);
        }

        // Phase D: when session var _debug=true, append a markdown debug block to
        // /content/additionalInfo so authors can watch state evolve live in the dialog.
        if (sessionVars.TryGetValue("_debug", out var dbg) && dbg is bool dflag && dflag)
        {
            AppendDebugBlock(patches, stateId, cursor, sessionVars);
        }

        var patchResult = await dialogporten.V1ServiceOwnerDialogsPatchDialog(blueprint.DialogId, patches, null, cancellationToken);
        if (!patchResult.IsSuccessful)
        {
            logger.LogWarning(
                "Dialogporten PATCH /dialogs/{DialogId} for stateId={StateId} cursor={Cursor} returned {StatusCode}. Body: {Body}",
                blueprint.DialogId, stateId, cursor, (int)patchResult.StatusCode, patchResult.Error?.Content);
            return BadRequest(patchResult.Error?.Content);
        }

        return Ok();
    }

    /// <summary>
    /// Evaluates a router stage's goto rules in order. Returns the target cursor of the first
    /// matching rule, or null if none matched (fall-through: the stage renders normally).
    /// A resolution failure is reported via <paramref name="error"/>.
    /// </summary>
    private int? ResolveGotoRules(
        IReadOnlyList<GotoRule> rules,
        IReadOnlyDictionary<string, object?> sessionVars,
        IReadOnlyDictionary<string, int> stageCursors,
        int cursor,
        out string? error)
    {
        error = null;
        foreach (var rule in rules)
        {
            if (rule.When is not null)
            {
                try
                {
                    if (Evaluator.Evaluate(rule.When, sessionVars) is not true)
                    {
                        continue;
                    }
                }
                catch (ScriptException ex)
                {
                    error = $"goto when evaluation failed at stage[{cursor}]: {ex.Message}";
                    return null;
                }
            }

            if (rule.Cursor is { } fixedCursor)
            {
                return fixedCursor;
            }

            // Computed target: session var holds a stage name.
            if (!sessionVars.TryGetValue(rule.Var!, out var raw) || raw is not string stageName || string.IsNullOrWhiteSpace(stageName))
            {
                error = $"goto @var({rule.Var}) at stage[{cursor}]: var does not hold a stage name";
                return null;
            }
            if (!stageCursors.TryGetValue(stageName, out var varCursor))
            {
                error = $"goto @var({rule.Var}) at stage[{cursor}]: '{stageName}' is not a defined stage";
                return null;
            }
            return varCursor;
        }
        return null;
    }

    private void FilterActionsByWhen(
        List<JsonPatchOperations_Operation> patches,
        IReadOnlyList<Expr?> actionWhens,
        IReadOnlyDictionary<string, object?> sessionVars)
    {
        if (actionWhens.Count == 0) return;

        foreach (var op in patches)
        {
            if (op.Path != "/guiActions") continue;

            var actionsNode = NormaliseToJsonArray(op.Value);
            if (actionsNode is null) continue;

            // Phase 1: evaluate expression whens. No when → always kept. `when: else` is
            // deferred to phase 2 (kept iff no expression when matched).
            var anyWithWhenMatched = false;
            var keep = new bool[actionsNode.Count];

            for (var i = 0; i < actionsNode.Count; i++)
            {
                var when = i < actionWhens.Count ? actionWhens[i] : null;
                if (when == null)
                {
                    keep[i] = true;
                    logger.LogInformation("[mutate]   action[{Index}] no when (always shown)", i);
                    continue;
                }
                if (when is ElseLit)
                {
                    logger.LogInformation("[mutate]   action[{Index}] when=else (fallback candidate)", i);
                    continue;
                }
                try
                {
                    var result = Evaluator.Evaluate(when, sessionVars);
                    var matched = result is bool b && b;
                    logger.LogInformation(
                        "[mutate]   action[{Index}] when={When} → {Result} (match={Matched})",
                        i, FormatExpr(when), result, matched);
                    if (matched)
                    {
                        keep[i] = true;
                        anyWithWhenMatched = true;
                    }
                }
                catch (ScriptException ex)
                {
                    logger.LogWarning("when expression evaluation failed: {Error}", ex.Message);
                }
            }

            // Phase 2: `when: else` actions render iff no expression-guarded action matched.
            for (var i = 0; i < actionsNode.Count; i++)
            {
                var when = i < actionWhens.Count ? actionWhens[i] : null;
                if (when is ElseLit && !anyWithWhenMatched)
                {
                    keep[i] = true;
                }
            }

            logger.LogInformation(
                "[mutate]   filter result: keep=[{Keep}] of {Total} actions",
                string.Join(",", Enumerable.Range(0, keep.Length).Where(i => keep[i])),
                actionsNode.Count);

            var filtered = new JsonArray();
            for (var i = 0; i < actionsNode.Count; i++)
            {
                if (keep[i])
                {
                    filtered.Add(actionsNode[i]?.DeepClone());
                }
            }
            op.Value = filtered;
        }
    }

    private static void AppendDebugBlock(
        List<JsonPatchOperations_Operation> patches,
        string stateId,
        int cursor,
        IReadOnlyDictionary<string, object?> sessionVars)
    {
        var op = patches.FirstOrDefault(p => p.Path == "/content/additionalInfo");
        if (op is null) return;

        var aiObject = NormaliseToJsonObject(op.Value);
        if (aiObject is null) return;

        var locArray = aiObject["value"] as JsonArray;
        if (locArray is null || locArray.Count == 0) return;

        var firstLoc = locArray[0] as JsonObject;
        if (firstLoc is null || firstLoc["value"] is not JsonValue) return;

        var existing = firstLoc["value"]!.GetValue<string>();

        var debugLines = new List<string>
        {
            existing,
            "",
            "---",
            "",
            "**[debug]**",
            "",
            $"- cursor: {cursor}",
            $"- stateId: `{stateId}`",
            "- vars:"
        };
        foreach (var (k, v) in sessionVars)
        {
            debugLines.Add($"  - {k}: {Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl.Evaluator.FormatVarForDisplay(v)}");
        }
        firstLoc["value"] = string.Join('\n', debugLines);

        // The Value field of the patch may be a JsonElement (from blueprint deserialisation)
        // or a JsonNode (when newly built). Either way we now write back the JsonObject so the
        // PATCH request carries the augmented additionalInfo.
        op.Value = aiObject;
    }

    private static JsonObject? NormaliseToJsonObject(object? value) => value switch
    {
        JsonObject jo => jo,
        JsonNode node when node.GetValueKind() == JsonValueKind.Object => node.AsObject(),
        JsonElement el when el.ValueKind == JsonValueKind.Object => JsonNode.Parse(el.GetRawText())?.AsObject(),
        _ => null
    };

    private static Random CreateRng(IReadOnlyDictionary<string, object?> sessionVars, string stateId)
    {
        if (sessionVars.TryGetValue("_seed", out var raw) && raw is string seedStr && !string.IsNullOrWhiteSpace(seedStr))
        {
            var combined = $"{seedStr}|{stateId}";
            var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(combined));
            var seedInt = BitConverter.ToInt32(hash, 0);
            return new Random(seedInt);
        }
        return Random.Shared;
    }

    private static JsonArray? NormaliseToJsonArray(object? value)
    {
        return value switch
        {
            JsonArray ja => ja,
            JsonNode node when node.GetValueKind() == JsonValueKind.Array => node.AsArray(),
            JsonElement el when el.ValueKind == JsonValueKind.Array => JsonNode.Parse(el.GetRawText())?.AsArray(),
            _ => null
        };
    }

    private static string FormatVars(IReadOnlyDictionary<string, object?> vars)
    {
        if (vars.Count == 0) return "{}";
        return "{" + string.Join(", ", vars.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}")) + "}";
    }

    private static string FormatValue(object? v) => v switch
    {
        null => "null",
        string s => $"\"{s}\"",
        System.Collections.IList list => "[" + string.Join(", ", list.Cast<object?>().Select(FormatValue)) + "]",
        _ => v.ToString() ?? ""
    };

    private static string FormatStmt(Stmt s) => s switch
    {
        SetStmt set => $"set {set.Var} = {FormatExpr(set.Value)}",
        IncStmt inc => inc.By is null ? $"inc {inc.Var}" : $"inc {inc.Var} by {FormatExpr(inc.By)}",
        DecStmt dec => dec.By is null ? $"dec {dec.Var}" : $"dec {dec.Var} by {FormatExpr(dec.By)}",
        ListAddStmt add => $"add {add.Var} += {FormatExpr(add.Value)}",
        ListRemoveStmt rem => $"remove {rem.Var} -= {FormatExpr(rem.Value)}",
        RollStmt roll => $"roll {roll.Var} = {FormatRollSpec(roll.Spec)}",
        IfStmt cond => $"if {FormatExpr(cond.Cond)} then {FormatStmt(cond.Then)}",
        _ => s.ToString() ?? ""
    };

    private static string FormatRollSpec(RollSpec spec) => spec switch
    {
        DiceSpec d => $"{d.Count}d{d.Sides}",
        RangeSpec r => $"{r.Lo}..{r.Hi}",
        _ => spec.ToString() ?? ""
    };

    private static string FormatExpr(Expr e) => e switch
    {
        IntLit i => i.Value.ToString(),
        BoolLit b => b.Value ? "true" : "false",
        StringLit s => $"\"{s.Value}\"",
        VarRef v => v.Name,
        UnaryNot n => $"!{FormatExpr(n.Operand)}",
        BinOp b => $"({FormatExpr(b.Left)} {b.Op} {FormatExpr(b.Right)})",
        Contains c => $"{c.ListVar} contains {FormatExpr(c.Value)}",
        ElseLit => "else",
        _ => e.ToString() ?? ""
    };
}
