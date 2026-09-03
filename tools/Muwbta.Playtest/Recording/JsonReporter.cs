using System.Text.Json;
using System.Text.Json.Serialization;

namespace Muwbta.Playtest.Recording;

/// <summary>
/// Writes the run as machine-readable JSON beside the page.
/// </summary>
/// <remarks>
/// Not for a person, and not a second copy of the report — this is what lets somebody diff two
/// runs, or count how often an expectation has been unmet across a fortnight, without parsing
/// prose. The raw SSE payload of every frame is kept here and nowhere else, so a question the
/// rendering did not anticipate can still be answered from the record.
/// </remarks>
public static class JsonReporter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(
        DateTimeOffset startedAt,
        string target,
        IReadOnlyList<ReportedPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var document = new
        {
            startedAt,
            target,
            plans = plans.Select(p => new
            {
                name = p.Outcome.Name,
                source = p.Outcome.SourcePath,
                about = p.About,
                actors = p.Outcome.Actors,
                met = p.Outcome.Met,
                unmet = p.Outcome.Unmet,
                problems = p.Outcome.Problems,
                entries = p.Transcript.Entries.Select(e => new
                {
                    at = e.At,
                    elapsed = e.Elapsed.TotalSeconds,
                    actor = e.Actor,
                    kind = e.Kind.ToString().ToLowerInvariant(),
                    text = e.Text,
                    met = e.Met,
                    raw = e.Raw,
                }),
            }),
        };

        return JsonSerializer.Serialize(document, Options);
    }
}
