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

        var patches = new JsonArray();
        foreach (var (_, stage) in stagesInOrder)
        {
            var ops = CompileStage(stage, language, cursorByName);
            var stageNode = JsonNode.Parse(JsonSerializer.Serialize(ops));
            patches.Add(stageNode);
        }

        var blueprint = new PlaybookBlueprint(
            Guid.Empty,
            patches,
            fceContents);

        return new DslCompileResult(
            source.Party,
            source.ServiceResource,
            blueprint,
            cursorByName[source.Start],
            source.Initial?.Title,
            source.Initial?.Summary,
            language);
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
            switch (raw)
            {
                case string s:
                    result[name] = new FceContent(FceMediaTypeDefault, s);
                    break;
                case IDictionary<object, object> d:
                    {
                        var mediaType = TryGetString(d, "media-type") ?? FceMediaTypeDefault;
                        var content = TryGetString(d, "content") ?? "";
                        result[name] = new FceContent(mediaType, content);
                        break;
                    }
                default:
                    throw new DslCompilationException($"fce['{name}'] must be a string or object");
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

    private static List<JsonPatchOperations_Operation> CompileStage(StageSource stage, string lang, Dictionary<string, int> cursorByName)
    {
        var ops = new List<JsonPatchOperations_Operation>();

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
            }
            ops.Add(MakeOp("add", "/guiActions", actionArray));
        }

        return ops;
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

    private static ActivitySource ParseActivity(object raw) => raw switch
    {
        string s => new ActivitySource { Type = s },
        IDictionary<object, object> d => new ActivitySource
        {
            Type = TryGetString(d, "type"),
            Description = TryGetString(d, "description"),
            By = TryGetString(d, "by")
        },
        _ => throw new DslCompilationException("activity must be a string or object")
    };

    private static ActionSource ParseAction(object raw) => raw switch
    {
        string s => ParseActionShorthand(s),
        IDictionary<object, object> d => new ActionSource
        {
            Label = TryGetString(d, "label"),
            Target = TryGetString(d, "target"),
            Priority = TryGetString(d, "priority")
        },
        _ => throw new DslCompilationException("action must be a string or object")
    };

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
}
