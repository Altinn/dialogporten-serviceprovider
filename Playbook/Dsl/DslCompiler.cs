using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;

public sealed class DslCompilationException(string message) : Exception(message);

public sealed record DslCompileResult(
    string Party,
    string ServiceResource,
    PlaybookBlueprint Blueprint,
    int InitialCursor,
    string? InitialTitle,
    string? InitialSummary,
    string Language);

public static class DslCompiler
{
    private const string FceMediaTypeDefault = "text/markdown";
    private const string MainContentReferenceMediaType = "application/vnd.dialogporten.frontchannelembed-url;type=text/markdown";

    public static DslCompileResult Compile(string yaml)
    {
        var source = ParseYaml(yaml);
        ValidateStructural(source);

        var stagesInOrder = source.Stages.ToList();
        var cursorByName = stagesInOrder
            .Select((kv, i) => (kv.Key, i))
            .ToDictionary(t => t.Key, t => t.i);

        var language = string.IsNullOrWhiteSpace(source.Language) ? "en" : source.Language;

        var fceContents = NormaliseFce(source.Fce);
        ValidateFceReferences(stagesInOrder, fceContents);

        var initialVars = NormaliseVars(source.Vars);
        var declaredVarNames = new HashSet<string>(initialVars.Keys);

        var patches = new JsonArray();
        var stageBehaviors = new List<StageBehavior>();
        foreach (var (_, stage) in stagesInOrder)
        {
            var (ops, behavior) = CompileStage(stage, language, cursorByName, declaredVarNames);
            var stageNode = JsonNode.Parse(JsonSerializer.Serialize(ops));
            patches.Add(stageNode);
            stageBehaviors.Add(behavior);
        }

        var blueprint = new PlaybookBlueprint(
            Guid.Empty,
            patches,
            fceContents,
            initialVars,
            stageBehaviors);

        return new DslCompileResult(
            source.Party,
            source.ServiceResource,
            blueprint,
            cursorByName[source.Start],
            source.Initial?.Title,
            source.Initial?.Summary,
            language);
    }

    private static IReadOnlyDictionary<string, object?> NormaliseVars(Dictionary<string, object?> raw)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (name, val) in raw)
        {
            result[name] = NormaliseVarValue(val);
        }
        return result;
    }

    private static object? NormaliseVarValue(object? raw) => raw switch
    {
        null => null,
        bool b => b,
        int i => (long)i,
        long l => l,
        string s => CoerceScalar(s),
        System.Collections.IList list => NormaliseList(list),
        _ => raw
    };

    /// <summary>
    /// YamlDotNet, when deserialising into a property typed as <c>object?</c>, returns scalar
    /// nodes as raw strings — no type inference. Coerce so <c>0</c>, <c>true</c>, etc. become
    /// their natural types before they hit the evaluator.
    /// </summary>
    private static object? CoerceScalar(string s)
    {
        if (s == "true") return true;
        if (s == "false") return false;
        if (s == "null" || s == "~") return null;
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }
        return s;
    }

    private static List<object?> NormaliseList(System.Collections.IList list)
    {
        var result = new List<object?>(list.Count);
        foreach (var item in list)
        {
            result.Add(NormaliseVarValue(item));
        }
        return result;
    }

    private static PlaybookSource ParseYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        PlaybookSource? root;
        try
        {
            // Top-level YAML is { playbook: {...} } — peel the wrapper.
            var wrapped = deserializer.Deserialize<Dictionary<string, PlaybookSource>>(yaml);
            if (wrapped == null || !wrapped.TryGetValue("playbook", out root) || root is null)
            {
                throw new DslCompilationException("YAML must have a top-level 'playbook' key");
            }
        }
        catch (DslCompilationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DslCompilationException($"YAML parse error: {ex.Message}");
        }

        return root;
    }

    private static void ValidateStructural(PlaybookSource source)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(source.Party)) problems.Add("playbook.party is required");
        if (string.IsNullOrWhiteSpace(source.ServiceResource)) problems.Add("playbook.service-resource is required");
        if (string.IsNullOrWhiteSpace(source.Start)) problems.Add("playbook.start is required");
        if (source.Stages.Count == 0) problems.Add("playbook.stages must have at least one stage");
        else if (!string.IsNullOrEmpty(source.Start) && !source.Stages.ContainsKey(source.Start))
            problems.Add($"playbook.start='{source.Start}' is not a defined stage name");
        if (problems.Count > 0) throw new DslCompilationException(string.Join("; ", problems));
    }

    private static Dictionary<string, FceContent> NormaliseFce(Dictionary<string, object?> rawFce)
    {
        var result = new Dictionary<string, FceContent>();
        foreach (var (name, raw) in rawFce)
        {
            if (raw is string s)
            {
                result[name] = new FceContent(FceMediaTypeDefault, s);
            }
            else
            {
                var d = AsStringDict(raw)
                    ?? throw new DslCompilationException($"fce['{name}'] must be a string or object (got {DescribeType(raw)})");
                RequireKnownKeys(d, FceKeys, $"fce['{name}']");
                var mediaType = TryGetString(d, "media-type") ?? FceMediaTypeDefault;
                var content = TryGetString(d, "content") ?? "";
                result[name] = new FceContent(mediaType, content);
            }
            if (!FceMediaTypes.IsAllowed(result[name].MediaType))
            {
                throw new DslCompilationException($"fce['{name}'] mediaType '{result[name].MediaType}' is not allowed (allowed: text/markdown, text/plain, text/html)");
            }
        }
        return result;
    }

    private static void ValidateFceReferences(List<KeyValuePair<string, StageSource>> stages, Dictionary<string, FceContent> defined)
    {
        var referenced = new HashSet<string>();
        foreach (var (_, stage) in stages)
        {
            if (!string.IsNullOrEmpty(stage.Content)) referenced.Add(stage.Content);
            if (stage.Transmissions != null)
                foreach (var tx in stage.Transmissions)
                    if (!string.IsNullOrEmpty(tx.Content)) referenced.Add(tx.Content);
        }

        var unknown = referenced.Where(r => !defined.ContainsKey(r)).ToList();
        if (unknown.Count > 0)
        {
            throw new DslCompilationException($"undefined FCE references: {string.Join(", ", unknown)}");
        }
    }

    private static (List<JsonPatchOperations_Operation> ops, StageBehavior behavior) CompileStage(
        StageSource stage, string lang, Dictionary<string, int> cursorByName, HashSet<string> declaredVars)
    {
        var ops = new List<JsonPatchOperations_Operation>();

        // Parse effects (Phase B). Strings → Stmt ASTs.
        var effects = new List<Stmt>();
        if (stage.Effects != null)
        {
            foreach (var effectText in stage.Effects)
            {
                Stmt stmt;
                try
                {
                    stmt = ScriptParser.ParseStatement(effectText);
                }
                catch (ScriptException ex)
                {
                    throw new DslCompilationException($"effect '{effectText}' parse error: {ex.Message}");
                }
                ValidateStmtVarRefs(stmt, declaredVars, effectText);
                effects.Add(stmt);
            }
        }

        // Per-action when expressions (Phase B).
        var actionWhens = new List<Expr?>();

        if (stage.Title != null)
        {
            ops.Add(MakeOp("replace", "/content/title/value/0/value", JsonValue.Create(stage.Title)));
        }
        if (stage.Summary != null)
        {
            ops.Add(MakeOp("replace", "/content/summary/value/0/value", JsonValue.Create(stage.Summary)));
        }
        if (stage.Status != null)
        {
            ops.Add(MakeOp("replace", "/status", JsonValue.Create(stage.Status)));
        }
        if (stage.ExtendedStatus != null)
        {
            ops.Add(MakeOp("add", "/content/extendedStatus",
                WrappedLocalisedContent("text/plain", stage.ExtendedStatus, lang)));
        }
        if (stage.AdditionalInfo != null)
        {
            ops.Add(MakeOp("add", "/content/additionalInfo",
                WrappedLocalisedContent("text/markdown", stage.AdditionalInfo, lang)));
        }
        if (stage.Content != null)
        {
            var fceUrl = $"{{baseUri}}/fce/named/{{stateId}}/{stage.Content}";
            ops.Add(MakeOp("add", "/content/mainContentReference",
                WrappedLocalisedContent(MainContentReferenceMediaType, fceUrl, lang)));
        }

        if (stage.Transmissions != null)
        {
            foreach (var tx in stage.Transmissions)
            {
                ops.Add(MakeOp("add", "/transmissions/-", CompileTransmission(tx, lang)));
            }
        }

        foreach (var act in NormaliseActivities(stage))
        {
            ops.Add(MakeOp("add", "/activities/-", CompileActivity(act, lang)));
        }

        if (stage.Actions is { Count: > 0 })
        {
            var actionArray = new JsonArray();
            for (var i = 0; i < stage.Actions.Count; i++)
            {
                var parsed = ParseAction(stage.Actions[i]);
                var resolvedTarget = ResolveTarget(parsed.Target ?? "", cursorByName);
                var priority = string.IsNullOrEmpty(parsed.Priority)
                    ? (i == 0 ? "primary" : i == 1 ? "secondary" : "tertiary")
                    : parsed.Priority;

                actionArray.Add(new JsonObject
                {
                    ["action"] = "write",
                    ["httpMethod"] = "POST",
                    ["url"] = resolvedTarget,
                    ["priority"] = priority,
                    ["title"] = new JsonArray { new JsonObject { ["languageCode"] = lang, ["value"] = parsed.Label } }
                });

                // Capture per-action when expression (Phase B).
                if (!string.IsNullOrWhiteSpace(parsed.When))
                {
                    Expr whenExpr;
                    try
                    {
                        whenExpr = ScriptParser.ParseExpression(parsed.When);
                    }
                    catch (ScriptException ex)
                    {
                        throw new DslCompilationException($"action when '{parsed.When}' parse error: {ex.Message}");
                    }
                    ValidateExprVarRefs(whenExpr, declaredVars, parsed.When);
                    actionWhens.Add(whenExpr);
                }
                else
                {
                    actionWhens.Add(null);
                }
            }
            ops.Add(MakeOp("add", "/guiActions", actionArray));
        }

        return (ops, new StageBehavior(effects, actionWhens));
    }

    private static void ValidateStmtVarRefs(Stmt stmt, HashSet<string> declared, string source)
    {
        switch (stmt)
        {
            case SetStmt s: Require(s.Var, declared, source); ValidateExprVarRefs(s.Value, declared, source); break;
            case IncStmt i: Require(i.Var, declared, source); if (i.By != null) ValidateExprVarRefs(i.By, declared, source); break;
            case DecStmt d: Require(d.Var, declared, source); if (d.By != null) ValidateExprVarRefs(d.By, declared, source); break;
            case ListAddStmt a: Require(a.Var, declared, source); ValidateExprVarRefs(a.Value, declared, source); break;
            case ListRemoveStmt r: Require(r.Var, declared, source); ValidateExprVarRefs(r.Value, declared, source); break;
        }
    }

    private static void ValidateExprVarRefs(Expr expr, HashSet<string> declared, string source)
    {
        switch (expr)
        {
            case VarRef v: Require(v.Name, declared, source); break;
            case UnaryNot n: ValidateExprVarRefs(n.Operand, declared, source); break;
            case BinOp b: ValidateExprVarRefs(b.Left, declared, source); ValidateExprVarRefs(b.Right, declared, source); break;
            case Contains c: Require(c.ListVar, declared, source); ValidateExprVarRefs(c.Value, declared, source); break;
        }
    }

    private static void Require(string varName, HashSet<string> declared, string source)
    {
        if (!declared.Contains(varName))
        {
            throw new DslCompilationException($"undeclared variable '{varName}' in '{source}' (declare it under playbook.vars)");
        }
    }

    private static JsonNode WrappedLocalisedContent(string mediaType, string value, string lang) =>
        new JsonObject
        {
            ["mediaType"] = mediaType,
            ["value"] = new JsonArray { new JsonObject { ["languageCode"] = lang, ["value"] = value } }
        };

    private static JsonNode CompileTransmission(TransmissionSource tx, string lang)
    {
        var sender = string.IsNullOrEmpty(tx.From) || string.Equals(tx.From, "ServiceOwner", StringComparison.Ordinal)
            ? (JsonNode)new JsonObject { ["actorType"] = "ServiceOwner" }
            : new JsonObject { ["actorType"] = "PartyRepresentative", ["actorName"] = tx.From };

        var content = new JsonObject();
        if (!string.IsNullOrEmpty(tx.Title))
            content["title"] = WrappedLocalisedContent("text/plain", tx.Title, lang);
        if (!string.IsNullOrEmpty(tx.Summary))
            content["summary"] = WrappedLocalisedContent("text/plain", tx.Summary, lang);
        if (!string.IsNullOrEmpty(tx.Content))
        {
            var fceUrl = $"{{baseUri}}/fce/named/{{stateId}}/{tx.Content}";
            content["contentReference"] = WrappedLocalisedContent(MainContentReferenceMediaType, fceUrl, lang);
        }

        return new JsonObject
        {
            ["type"] = tx.Type,
            ["sender"] = sender,
            ["content"] = content
        };
    }

    private static JsonNode CompileActivity(ActivitySource act, string lang)
    {
        var performedBy = string.IsNullOrEmpty(act.By) || string.Equals(act.By, "ServiceOwner", StringComparison.Ordinal)
            ? (JsonNode)new JsonObject { ["actorType"] = "ServiceOwner" }
            : new JsonObject { ["actorType"] = "PartyRepresentative", ["actorName"] = act.By };

        var obj = new JsonObject
        {
            ["type"] = act.Type,
            ["performedBy"] = performedBy
        };

        if (!string.IsNullOrEmpty(act.Description))
        {
            obj["description"] = new JsonArray { new JsonObject { ["languageCode"] = lang, ["value"] = act.Description } };
        }

        return obj;
    }

    private static IEnumerable<ActivitySource> NormaliseActivities(StageSource stage)
    {
        if (stage.Activity != null)
        {
            yield return ParseActivity(stage.Activity);
        }
        if (stage.Activities != null)
        {
            foreach (var raw in stage.Activities)
            {
                yield return ParseActivity(raw);
            }
        }
    }

    private static readonly HashSet<string> ActivityKeys = new(StringComparer.OrdinalIgnoreCase)
        { "type", "description", "by" };
    private static readonly HashSet<string> ActionKeys = new(StringComparer.OrdinalIgnoreCase)
        { "label", "target", "priority", "when" };
    private static readonly HashSet<string> FceKeys = new(StringComparer.OrdinalIgnoreCase)
        { "media-type", "content" };

    private static ActivitySource ParseActivity(object raw)
    {
        if (raw is string s) return new ActivitySource { Type = s };
        var d = AsStringDict(raw)
            ?? throw new DslCompilationException($"activity must be a string or object (got {DescribeType(raw)})");
        RequireKnownKeys(d, ActivityKeys, "activity");
        return new ActivitySource
        {
            Type = TryGetString(d, "type"),
            Description = TryGetString(d, "description"),
            By = TryGetString(d, "by")
        };
    }

    private static ActionSource ParseAction(object raw)
    {
        if (raw is string s) return ParseActionShorthand(s);
        var d = AsStringDict(raw)
            ?? throw new DslCompilationException($"action must be a string or object (got {DescribeType(raw)})");
        RequireKnownKeys(d, ActionKeys, "action");
        return new ActionSource
        {
            Label = TryGetString(d, "label"),
            Target = TryGetString(d, "target"),
            Priority = TryGetString(d, "priority"),
            When = TryGetString(d, "when")
        };
    }

    /// <summary>
    /// YamlDotNet may return either <c>IDictionary&lt;object,object&gt;</c> or
    /// <c>IDictionary&lt;string,object&gt;</c> for an untyped mapping, depending on context
    /// and library version. Normalise both (plus the non-generic <c>IDictionary</c>) to a
    /// single string-keyed view so downstream code doesn't have to care.
    /// </summary>
    private static Dictionary<string, object?>? AsStringDict(object? raw)
    {
        if (raw is not System.Collections.IDictionary id) return null;
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in id.Keys)
        {
            if (key is string k) result[k] = id[key];
        }
        return result;
    }

    private static void RequireKnownKeys(Dictionary<string, object?> dict, HashSet<string> known, string context)
    {
        var unknown = dict.Keys.Where(k => !known.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new DslCompilationException(
                $"{context}: unknown key(s) {string.Join(", ", unknown.Select(k => $"'{k}'"))}. " +
                $"Known: {string.Join(", ", known.OrderBy(x => x))}");
        }
    }

    private static string DescribeType(object? raw) => raw is null ? "null" : raw.GetType().FullName ?? raw.GetType().Name;

    private static ActionSource ParseActionShorthand(string s)
    {
        var unicodeIdx = s.IndexOf('→'); // →
        var arrowIdx = unicodeIdx >= 0 ? unicodeIdx : s.IndexOf("->", StringComparison.Ordinal);
        if (arrowIdx < 0)
        {
            throw new DslCompilationException($"action shorthand '{s}' missing '→' or '->'");
        }
        var label = s[..arrowIdx].TrimEnd();
        var skip = unicodeIdx >= 0 ? 1 : 2;
        var target = s[(arrowIdx + skip)..].TrimStart();
        return new ActionSource { Label = label, Target = target };
    }

    private static string ResolveTarget(string target, Dictionary<string, int> stageCursors)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new DslCompilationException("action target is empty");

        if (target.StartsWith('$')) return target;

        // Random: ?(a, b, c) or ?(a:1, b:3) — uniform or weighted random pick.
        if (target.StartsWith("?(") && target.EndsWith(')'))
        {
            return CompileRandomTarget(target, stageCursors);
        }

        switch (target)
        {
            case "next": return "$next";
            case "previous": return "$previous";
            case "restart": return "$goto=0";
        }

        if (stageCursors.TryGetValue(target, out var cursor))
        {
            return $"$goto={cursor}";
        }

        throw new DslCompilationException($"unknown action target '{target}' (not a defined stage, magic word, or $-command)");
    }

    private static string CompileRandomTarget(string target, Dictionary<string, int> stageCursors)
    {
        var inner = target[2..^1];
        var resolved = new List<string>();
        foreach (var raw in inner.Split(','))
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;
            var parts = entry.Split(':');
            var name = parts[0].Trim();
            var weight = 1;
            if (parts.Length > 1)
            {
                if (!int.TryParse(parts[1].Trim(), out weight) || weight < 1)
                {
                    throw new DslCompilationException($"random target weight must be a positive integer (got '{parts[1]}' for '{name}')");
                }
            }
            if (!stageCursors.TryGetValue(name, out var cursor))
            {
                throw new DslCompilationException($"random target references undefined stage '{name}'");
            }
            resolved.Add($"{cursor}:{weight}");
        }
        if (resolved.Count == 0)
        {
            throw new DslCompilationException($"random target '{target}' has no entries");
        }
        return "$random=" + string.Join("|", resolved);
    }

    private static JsonPatchOperations_Operation MakeOp(string op, string path, JsonNode? value)
    {
        var opType = op switch
        {
            "add" => JsonPatchOperations_OperationType.Add,
            "replace" => JsonPatchOperations_OperationType.Replace,
            "remove" => JsonPatchOperations_OperationType.Remove,
            _ => throw new DslCompilationException($"unsupported op '{op}'")
        };

        return new JsonPatchOperations_Operation
        {
            Op = op,
            OperationType = opType,
            Path = path,
            Value = value!
        };
    }

    private static string? TryGetString(IDictionary<object, object> d, string key)
    {
        if (d.TryGetValue(key, out var v) && v is string s) return s;
        return null;
    }

    private static string? TryGetString(Dictionary<string, object?> d, string key)
    {
        if (d.TryGetValue(key, out var v) && v is string s) return s;
        return null;
    }
}
