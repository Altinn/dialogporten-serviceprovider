using System.Text.Json;
using System.Text.RegularExpressions;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public static class PlaybookSampleValidator
{
    private const int ExtendedStatusMaxLength = 25;

    private static readonly Regex GotoRegex = new(@"\$goto=(\d+)", RegexOptions.Compiled);
    private static readonly Regex FceRefRegex = new(@"/fce/named/\{stateId\}/([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(JsonDocument doc)
    {
        var issues = new List<string>();
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            issues.Add("top-level JSON must be an object");
            return issues;
        }

        if (!TryGet(root, "playbookState", out var stateEl) || stateEl.ValueKind != JsonValueKind.Object)
        {
            issues.Add("missing or non-object 'playbookState'");
            return issues;
        }

        if (!TryGet(stateEl, "Patches", out var patchesEl) || patchesEl.ValueKind != JsonValueKind.Array)
        {
            issues.Add("missing or non-array 'playbookState.Patches'");
            return issues;
        }

        var patches = patchesEl.EnumerateArray().ToList();
        if (patches.Count == 0)
        {
            issues.Add("'playbookState.Patches' is empty");
        }

        if (TryGet(stateEl, "Cursor", out var cursorEl) && cursorEl.TryGetInt32(out var cursor))
        {
            if (cursor < 0 || cursor >= patches.Count)
            {
                issues.Add($"playbookState.Cursor {cursor} is out of range (valid: 0..{patches.Count - 1})");
            }
        }

        var fceContents = new Dictionary<string, string>();
        if (TryGet(root, "fceContents", out var fceEl) && fceEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var fce in fceEl.EnumerateObject())
            {
                if (fce.Value.ValueKind != JsonValueKind.Object)
                {
                    issues.Add($"fceContents['{fce.Name}'] is not an object");
                    continue;
                }
                if (!TryGet(fce.Value, "mediaType", out var mtEl) || mtEl.ValueKind != JsonValueKind.String)
                {
                    issues.Add($"fceContents['{fce.Name}'] is missing 'mediaType'");
                    continue;
                }
                var mediaType = mtEl.GetString();
                if (!FceMediaTypes.IsAllowed(mediaType))
                {
                    issues.Add($"fceContents['{fce.Name}'] has disallowed mediaType '{mediaType}' (allowed: text/markdown, text/plain, text/html)");
                }
                fceContents[fce.Name] = mediaType ?? "";
            }
        }

        var referencedFces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < patches.Count; i++)
        {
            var stage = patches[i];
            if (stage.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"stage {i}: not an array");
                continue;
            }

            foreach (var op in stage.EnumerateArray())
            {
                ValidateOp(i, op, issues);
            }

            var raw = stage.GetRawText();
            foreach (Match m in GotoRegex.Matches(raw))
            {
                var target = int.Parse(m.Groups[1].Value);
                if (target < 0 || target >= patches.Count)
                {
                    issues.Add($"stage {i}: $goto={target} out of range (valid: 0..{patches.Count - 1})");
                }
            }
            foreach (Match m in FceRefRegex.Matches(raw))
            {
                referencedFces.Add(m.Groups[1].Value);
            }
        }

        foreach (var refName in referencedFces)
        {
            if (!fceContents.ContainsKey(refName))
            {
                issues.Add($"FCE reference '{refName}' has no entry in fceContents");
            }
        }
        foreach (var defined in fceContents.Keys)
        {
            if (!referencedFces.Contains(defined))
            {
                issues.Add($"fceContents['{defined}'] is defined but never referenced");
            }
        }

        return issues;
    }

    private static void ValidateOp(int stageIndex, JsonElement op, List<string> issues)
    {
        if (op.ValueKind != JsonValueKind.Object) return;
        if (!TryGet(op, "path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String) return;
        var path = pathEl.GetString();
        if (!TryGet(op, "value", out var valueEl)) return;

        switch (path)
        {
            case "/content/extendedStatus":
                ValidateExtendedStatus(stageIndex, valueEl, issues);
                break;
            case "/activities/-":
                ValidateActivity(stageIndex, valueEl, issues);
                break;
            case "/guiActions":
                ValidateGuiActions(stageIndex, valueEl, issues);
                break;
        }
    }

    private static void ValidateGuiActions(int stageIndex, JsonElement value, List<string> issues)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        var idx = 0;
        foreach (var action in value.EnumerateArray())
        {
            if (action.ValueKind != JsonValueKind.Object) { idx++; continue; }

            var actionVerb = TryGet(action, "action", out var actionEl) && actionEl.ValueKind == JsonValueKind.String
                ? actionEl.GetString()
                : null;
            if (actionVerb != "write")
            {
                issues.Add($"stage {stageIndex}: guiActions[{idx}].action is '{actionVerb ?? "(missing)"}' (must be 'write' for mutating playbook actions)");
            }

            var httpMethod = TryGet(action, "httpMethod", out var methodEl) && methodEl.ValueKind == JsonValueKind.String
                ? methodEl.GetString()
                : null;
            if (httpMethod != "POST")
            {
                issues.Add($"stage {stageIndex}: guiActions[{idx}].httpMethod is '{httpMethod ?? "(missing — defaults to GET)"}' (must be 'POST'; Arbeidsflate treats GET as plain navigation, not a mutation)");
            }

            idx++;
        }
    }

    private static void ValidateExtendedStatus(int stageIndex, JsonElement value, List<string> issues)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        if (!TryGet(value, "value", out var locsEl) || locsEl.ValueKind != JsonValueKind.Array) return;
        foreach (var loc in locsEl.EnumerateArray())
        {
            if (TryGet(loc, "value", out var locValueEl) && locValueEl.ValueKind == JsonValueKind.String)
            {
                var s = locValueEl.GetString() ?? "";
                if (s.Length > ExtendedStatusMaxLength)
                {
                    issues.Add($"stage {stageIndex}: extendedStatus '{s}' is {s.Length} chars (max {ExtendedStatusMaxLength})");
                }
            }
        }
    }

    private static void ValidateActivity(int stageIndex, JsonElement value, List<string> issues)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        if (!TryGet(value, "type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return;
        var type = typeEl.GetString() ?? "";
        var hasDescription = TryGet(value, "description", out var descEl)
            && descEl.ValueKind == JsonValueKind.Array
            && descEl.GetArrayLength() > 0;

        if (type == "Information" && !hasDescription)
        {
            issues.Add($"stage {stageIndex}: activity type 'Information' is missing required 'description'");
        }
        else if (type != "Information" && hasDescription)
        {
            issues.Add($"stage {stageIndex}: activity type '{type}' must not include 'description' (only allowed when type is 'Information')");
        }
    }

    private static bool TryGet(JsonElement el, string name, out JsonElement value)
    {
        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
