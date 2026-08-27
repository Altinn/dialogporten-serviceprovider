using System.Globalization;
using System.Text.RegularExpressions;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

/// <summary>
/// Placeholders resolved when a named front channel embed body is served, so an embed can address
/// this service back: <c>{baseUri}</c>, <c>{stateId}</c>, <c>{cursor:STAGE}</c> and
/// <c>{formAction:STAGE}</c> — the last expanding to the URL an embedded HTML form posts to in
/// order to advance the playbook to STAGE.
/// Kept apart from the <c>{vars.X}</c>/<c>{if:}</c> templating in <see cref="Dsl.Evaluator"/>:
/// these depend on the request and the blueprint, not on session state.
/// </summary>
public static class EmbedPlaceholders
{
    /// <summary>Matches <c>{formAction:stage-name}</c> and <c>{cursor:stage-name}</c>.</summary>
    private static readonly Regex StageReference = new(@"\{(formAction|cursor):([^}\s]+)\}", RegexOptions.Compiled);

    public static string Render(
        string body,
        string baseUri,
        string stateId,
        IReadOnlyDictionary<string, int> stageCursors)
    {
        var rendered = StageReference.Replace(body, match =>
        {
            var stage = match.Groups[2].Value;
            if (!stageCursors.TryGetValue(stage, out var cursor))
            {
                // Compile-time validation normally catches this; a blueprint created through the
                // JSON API has no stage names at all, so leave a visible marker rather than throw.
                return $"[unknown stage '{stage}']";
            }

            return match.Groups[1].Value == "cursor"
                ? cursor.ToString(CultureInfo.InvariantCulture)
                : FormActionUrl(baseUri, stateId, cursor);
        });

        return rendered
            .Replace("{baseUri}", baseUri.TrimEnd('/'), StringComparison.Ordinal)
            .Replace("{stateId}", stateId, StringComparison.Ordinal);
    }

    public static string FormActionUrl(string baseUri, string stateId, int cursor) =>
        $"{baseUri.TrimEnd('/')}/mutate/form/{stateId}/{cursor.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Stage names referenced by <c>{formAction:…}</c>/<c>{cursor:…}</c> in a body, so the DSL
    /// compiler can reject a reference to a stage that does not exist.
    /// </summary>
    public static IEnumerable<string> ReferencedStages(string? body) =>
        string.IsNullOrEmpty(body)
            ? []
            : StageReference.Matches(body).Select(m => m.Groups[2].Value);
}
