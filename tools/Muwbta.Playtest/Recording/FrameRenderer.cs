using System.Text;
using System.Text.Json;
using Muwbta.Playtest.Session;

namespace Muwbta.Playtest.Recording;

/// <summary>
/// Turns a protocol frame back into what a player would have read.
/// </summary>
/// <remarks>
/// The apparatus records prose, not JSON, because the whole reason it exists is that some bugs are
/// only visible as prose. <em>"Your Kick takes effect!"</em> passed every assertion in the suite and
/// was obviously wrong the moment somebody read it. A transcript of payloads would have hidden it
/// again.
///
/// Mirrors <c>client/src/net/protocol.ts</c>, which is the other hand-written copy of this surface.
/// The raw payload is kept alongside every rendered line, so nothing is actually lost by rendering.
/// </remarks>
public static class FrameRenderer
{
    /// <summary>
    /// The frame as one readable line, or null for frames a player never reads as text.
    /// </summary>
    public static (EntryKind Kind, string Text)? Render(SseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        try
        {
            return frame.EventType switch
            {
                "text" => (EntryKind.Text, Prose(frame.Json)),
                "vitals" => (EntryKind.Vitals, VitalsText(frame.Json)),
                "sys" => (EntryKind.Sys, SysText(frame.Json)),

                // Panels, not scrollback. Recorded raw so nothing is lost, but kept out of the
                // readable transcript because a player reads them as panels beside the text.
                //
                // `room` belongs here despite carrying prose, and the first run of this apparatus
                // proved why: `SendRoom` deliberately sends the title, description and exits
                // *twice* - once as this structured frame for the panel, and once as text spans,
                // because PLAN.md §5 makes the scrollback authoritative so a player who ignores
                // the map misses nothing. Rendering both would double-print every room in every
                // transcript, and the duplicate reads exactly like an engine bug. It is not one.
                "room" => (EntryKind.Frame, RoomText(frame.Json)),
                "map" => (EntryKind.Frame, $"[map {Size(frame.Json)}]"),
                "contents" => (EntryKind.Frame, ContentsText(frame.Json)),

                _ => (EntryKind.Frame, $"[{frame.EventType}]"),
            };
        }
        catch (JsonException)
        {
            // A frame this build cannot parse is still evidence. Recording it as raw beats
            // dropping it, since an unparseable frame is itself a finding.
            return (EntryKind.Frame, $"[unparseable {frame.EventType}: {frame.Data}]");
        }
    }

    /// <summary>Concatenates the spans, which is exactly what the client renders.</summary>
    private static string Prose(JsonElement payload)
    {
        if (!payload.TryGetProperty("spans", out var spans))
        {
            return string.Empty;
        }

        var text = new StringBuilder();

        foreach (var span in spans.EnumerateArray())
        {
            if (span.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String)
            {
                text.Append(t.GetString());
            }
        }

        return text.ToString();
    }

    private static string RoomText(JsonElement payload)
    {
        var title = Str(payload, "title");
        var description = Str(payload, "description");
        var exits = payload.TryGetProperty("exits", out var e) && e.ValueKind == JsonValueKind.Array
            ? string.Join(", ", e.EnumerateArray().Select(x => x.GetString()))
            : string.Empty;

        var text = new StringBuilder(title);

        if (description.Length > 0)
        {
            text.Append('\n').Append(description);
        }

        if (exits.Length > 0)
        {
            text.Append("\nExits: ").Append(exits);
        }

        return text.ToString();
    }

    /// <summary>
    /// Compact, because vitals arrive whenever anything moves and a fight produces one per blow.
    /// Kept in the transcript rather than filtered out: a reviewer reading a combat plan is
    /// usually asking what the health bar did.
    /// </summary>
    private static string VitalsText(JsonElement payload) =>
        $"hp {Num(payload, "health")}/{Num(payload, "healthMax")} " +
        $"fp {Num(payload, "focus")}/{Num(payload, "focusMax")} " +
        $"sp {Num(payload, "stamina")}/{Num(payload, "staminaMax")} " +
        $"lvl {Num(payload, "level")} xp {Num(payload, "xp")} gold {Num(payload, "gold")}";

    private static string SysText(JsonElement payload) =>
        $"({Str(payload, "kind")}) {Str(payload, "message")}";

    private static string ContentsText(JsonElement payload)
    {
        var occupants = Labels(payload, "occupants");
        var items = Labels(payload, "items");

        return $"[here: {(occupants.Length == 0 ? "-" : occupants)} | " +
               $"items: {(items.Length == 0 ? "-" : items)}]";
    }

    private static string Labels(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? string.Join(", ", list.EnumerateArray().Select(x => Str(x, "label")))
            : string.Empty;

    private static string Size(JsonElement payload) =>
        $"{Num(payload, "w")}x{Num(payload, "h")}";

    private static string Str(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Num(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.ToString()
            : "?";
}
