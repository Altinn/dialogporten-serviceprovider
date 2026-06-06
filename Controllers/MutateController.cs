using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;
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
    IServiceownerApi dialogporten,
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

        // Phase B: apply this stage's effects to session vars, then persist.
        var sessionVars = await stateStore.GetSessionVarsAsync(stateId, cancellationToken);
        logger.LogInformation(
            "[mutate] stateId={StateId} cursor={Cursor} entry-vars={Vars} blueprint-has-{NumBehaviors}-stage-behaviors",
            stateId, cursor, FormatVars(sessionVars), blueprint.StageBehaviors.Count);

        if (cursor < blueprint.StageBehaviors.Count)
        {
            var stageBehavior = blueprint.StageBehaviors[cursor];
            logger.LogInformation(
                "[mutate]   stage[{Cursor}] effects={EffectCount} action-whens={WhenCount}",
                cursor, stageBehavior.Effects.Count, stageBehavior.ActionWhens.Count(w => w != null));

            foreach (var stmt in stageBehavior.Effects)
            {
                try
                {
                    var before = sessionVars;
                    sessionVars = Evaluator.Apply(stmt, sessionVars);
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
            if (stageBehavior.Effects.Count > 0)
            {
                await stateStore.UpdateSessionVarsAsync(stateId, sessionVars, cancellationToken);
            }
        }
        else
        {
            logger.LogInformation(
                "[mutate]   NO stage behaviors at cursor={Cursor} (blueprint StageBehaviors.Count={Count}); Phase B inactive for this playbook",
                cursor, blueprint.StageBehaviors.Count);
        }

        var compiler = new PlaybookCompiler(options.Value)
        {
            Progress = 0
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

        var patches = await compiler.CompilePatches(stateId, blueprint, cursor);
        if (patches.Count == 0)
        {
            return BadRequest();
        }

        // Phase B: filter /guiActions by per-action when expressions using current session vars.
        if (cursor < blueprint.StageBehaviors.Count)
        {
            FilterActionsByWhen(patches, blueprint.StageBehaviors[cursor].ActionWhens, sessionVars);
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

            // Phase 1: evaluate each action's when; track which match and whether any with-when matched.
            var anyWithWhenMatched = false;
            var keep = new bool[actionsNode.Count];

            for (var i = 0; i < actionsNode.Count; i++)
            {
                var when = i < actionWhens.Count ? actionWhens[i] : null;
                if (when == null)
                {
                    logger.LogInformation("[mutate]   action[{Index}] no when (fallback candidate)", i);
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

            // Phase 2: fallbacks render iff no with-when matched.
            for (var i = 0; i < actionsNode.Count; i++)
            {
                var when = i < actionWhens.Count ? actionWhens[i] : null;
                if (when == null && !anyWithWhenMatched)
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
        _ => s.ToString() ?? ""
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
        _ => e.ToString() ?? ""
    };
}
