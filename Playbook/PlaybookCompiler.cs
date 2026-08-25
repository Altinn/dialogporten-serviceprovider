using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

/// <param name="mutateBaseUri">
/// Base URI that <c>{baseUri}</c>, mutate URLs and FCE URLs are built from, ie. where this service
/// provider is reachable from the browser showing Arbeidsflate. Comes from the playbook's target,
/// so a playbook keeps emitting the URLs it was created with.
/// </param>
public class PlaybookCompiler(string mutateBaseUri)
{
    private readonly string _baseUri = mutateBaseUri.TrimEnd('/');
    private const int MaxDepth = 32;
    private const string FceUrlMarker = "/fce/named/";
    private const int MaxNodes = 1000;
    private int _visitedNodes;

    public int Progress { get; set; } = 0;

    /// <summary>
    /// RNG used for resolving <c>$random=...</c> targets. Default is process-shared (true randomness).
    /// Callers may inject a seeded <see cref="Random"/> for deterministic playback (Phase C <c>_seed</c>).
    /// </summary>
    public Random Rng { get; set; } = Random.Shared;

    /// <summary>
    /// Phase D: session variables consulted for <c>{vars.X}</c> interpolation in any string-typed
    /// patch value. Empty dictionary = no interpolation performed.
    /// </summary>
    public IReadOnlyDictionary<string, object?> SessionVars { get; set; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// Phase E: stage-name → cursor map used to resolve <c>$gotovar=NAME</c> commands, where
    /// session var NAME holds a stage name. Empty dictionary = computed targets unresolvable.
    /// </summary>
    public IReadOnlyDictionary<string, int> StageCursors { get; set; } =
        new Dictionary<string, int>();

    public Task<List<JsonPatchOperations_Operation>> CompilePatches(string stateId, PlaybookBlueprint blueprint, int cursor)
    {
        _visitedNodes = 0;
        if (cursor < 0 || cursor >= blueprint.Patches.Count)
        {
            return Task.FromResult<List<JsonPatchOperations_Operation>>([]);
        }

        var stagePatches = blueprint.Patches[cursor]?.Deserialize<List<JsonPatchOperations_Operation>>();
        return CompileStageAsync(stagePatches, stateId, cursor);
    }

    private async Task<List<JsonPatchOperations_Operation>> CompileStageAsync(List<JsonPatchOperations_Operation>? stagePatches, string stateId, int cursor)
    {
        List<JsonPatchOperations_Operation> compiled = [];
        if (stagePatches == null) return compiled;

        foreach (var patch in stagePatches)
        {
            var rewritten = await CompilePatch(patch, stateId, cursor);
            compiled.Add(rewritten ?? patch);
        }
        return compiled;
    }

    private async Task<JsonPatchOperations_Operation?> CompilePatch(JsonPatchOperations_Operation patch, string stateId, int cursor)
    {
        switch (patch.Value)
        {
            case JsonElement { ValueKind: JsonValueKind.String } stringValue:
                {
                    var raw = stringValue.GetString();
                    if (Lexer.TryParseCommand(raw, out var command))
                    {
                        return CreateUpdatedPatch(patch, command, stateId, cursor);
                    }
                    if (raw != null && ContainsPlaceholder(raw))
                    {
                        return new JsonPatchOperations_Operation
                        {
                            OperationType = patch.OperationType,
                            Path = patch.Path,
                            Op = patch.Op,
                            From = patch.From,
                            Value = JsonValue.Create(SubstitutePlaceholders(raw, stateId))
                        };
                    }
                    break;
                }
            case JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array } objectValue:
                {
                    var updated = await ProcessJsonElement(objectValue, stateId, cursor, 0);
                    if (updated.HasValue)
                    {
                        return new JsonPatchOperations_Operation
                        {
                            OperationType = patch.OperationType,
                            Path = patch.Path,
                            Op = patch.Op,
                            From = patch.From,
                            Value = updated.Value
                        };
                    }
                    break;
                }
        }
        return null;
    }

    private int ComputeNextCursor(Command command, int currentCursor)
    {
        return command.Type switch
        {
            CommandType.Next => currentCursor + 1,
            CommandType.Previous => currentCursor - 1,
            CommandType.Goto => (int)command.Value,
            CommandType.GotoIfProgress => ResolveGotoIfProgress((GotoIfProgressValue)command.Value),
            CommandType.Random => PickWeighted((RandomValue)command.Value),
            CommandType.GotoVar => ResolveGotoVar((string)command.Value),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private int ResolveGotoVar(string varName)
    {
        if (!SessionVars.TryGetValue(varName, out var raw))
        {
            throw new InvalidOperationException($"$gotovar: session var '{varName}' is not defined");
        }
        if (raw is not string stageName || string.IsNullOrWhiteSpace(stageName))
        {
            throw new InvalidOperationException($"$gotovar: session var '{varName}' does not hold a stage name (value: '{raw}')");
        }
        if (!StageCursors.TryGetValue(stageName, out var cursor))
        {
            throw new InvalidOperationException($"$gotovar: session var '{varName}' holds '{stageName}', which is not a defined stage");
        }
        return cursor;
    }

    private int PickWeighted(RandomValue rv)
    {
        var total = rv.Choices.Sum(c => c.Weight);
        var roll = Rng.Next(0, total);
        var acc = 0;
        foreach (var (cursor, weight) in rv.Choices)
        {
            acc += weight;
            if (roll < acc) return cursor;
        }
        return rv.Choices[^1].Cursor;
    }

    private int ResolveGotoIfProgress(GotoIfProgressValue value) =>
        Progress == value.Progress ? value.Goto : value.Else;

    private string BuildMutateUrl(string stateId, int cursor) =>
        $"{_baseUri}/mutate/{stateId}/{cursor}";

    private static bool ContainsPlaceholder(string raw) =>
        raw.Contains("{baseUri}") || raw.Contains("{stateId}") || raw.Contains("{vars.")
        || raw.Contains("{if:", StringComparison.Ordinal)
        || raw.Contains(FceUrlMarker, StringComparison.Ordinal);

    private string SubstitutePlaceholders(string raw, string stateId) =>
        AppendCacheBuster(
            Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl.Evaluator.RenderTemplate(
                raw.Replace("{baseUri}", _baseUri).Replace("{stateId}", stateId),
                SessionVars));

    /// <summary>
    /// Arbeidsflate only reloads an FCE iframe when its URL actually changes. Since an FCE body can
    /// change without its URL changing (same named FCE re-referenced by a later stage, or
    /// <c>{vars.X}</c> interpolation inside the body), every compiled FCE URL gets a fresh random
    /// query parameter. Deliberately uses <see cref="Random.Shared"/> rather than <see cref="Rng"/>:
    /// cache busting must stay unique even during deterministic seeded playback.
    /// </summary>
    private static string AppendCacheBuster(string url)
    {
        if (!url.Contains(FceUrlMarker, StringComparison.Ordinal))
        {
            return url;
        }

        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}_cb={Random.Shared.Next():x8}";
    }

    private bool ExceedsLimits(int depth)
    {
        if (depth > MaxDepth)
        {
            return true;
        }

        _visitedNodes++;
        return _visitedNodes > MaxNodes;
    }

    private async Task<JsonElement?> ProcessJsonElement(JsonElement element, string stateId, int cursor, int depth)
    {
        if (ExceedsLimits(depth))
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var stringValue = element.GetString();
                if (Lexer.TryParseCommand(stringValue, out var command))
                {
                    var nextCursor = ComputeNextCursor(command, cursor);
                    return JsonSerializer.SerializeToElement(BuildMutateUrl(stateId, nextCursor));
                }
                if (stringValue != null && ContainsPlaceholder(stringValue))
                {
                    return JsonSerializer.SerializeToElement(SubstitutePlaceholders(stringValue, stateId));
                }
                return null;

            case JsonValueKind.Object:
                return await ProcessObject(element, stateId, cursor, depth + 1);

            case JsonValueKind.Array:
                return await ProcessArray(element, stateId, cursor, depth + 1);

            case JsonValueKind.Undefined:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:
                return null;
        }
    }

    private async Task<JsonElement?> ProcessObject(JsonElement objectValue, string stateId, int cursor, int depth)
    {
        var updates = new Dictionary<string, JsonElement>();
        var hasChanges = false;

        foreach (var property in objectValue.EnumerateObject())
        {
            var value = property.Value;
            var updated = await ProcessJsonElement(property.Value, stateId, cursor, depth);
            if (updated.HasValue)
            {
                value = updated.Value;
                hasChanges = true;
            }

            updates[property.Name] = value;
        }

        if (!hasChanges)
        {
            return null;
        }

        using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        foreach (var kvp in updates)
        {
            writer.WritePropertyName(kvp.Key);
            kvp.Value.WriteTo(writer);
        }
        writer.WriteEndObject();
        await writer.FlushAsync();

        var jsonBytes = stream.ToArray();
        return JsonDocument.Parse(jsonBytes).RootElement;
    }

    private async Task<JsonElement?> ProcessArray(JsonElement arrayValue, string stateId, int cursor, int depth)
    {
        var updates = new List<JsonElement>();
        var hasChanges = false;

        foreach (var item in arrayValue.EnumerateArray())
        {
            var value = item;
            var updated = await ProcessJsonElement(item, stateId, cursor, depth);
            if (updated.HasValue)
            {
                value = updated.Value;
                hasChanges = true;
            }

            updates.Add(value);
        }

        if (!hasChanges)
        {
            return null;
        }

        using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartArray();
        foreach (var element in updates)
        {
            element.WriteTo(writer);
        }
        writer.WriteEndArray();
        await writer.FlushAsync();

        var jsonBytes = stream.ToArray();
        return JsonDocument.Parse(jsonBytes).RootElement;
    }

    private JsonPatchOperations_Operation CreateUpdatedPatch(JsonPatchOperations_Operation patch, Command command, string stateId, int cursor)
    {
        var nextCursor = ComputeNextCursor(command, cursor);
        return new JsonPatchOperations_Operation
        {
            OperationType = patch.OperationType,
            Path = patch.Path,
            Op = patch.Op,
            From = patch.From,
            Value = JsonValue.Create(BuildMutateUrl(stateId, nextCursor))
        };
    }
}
