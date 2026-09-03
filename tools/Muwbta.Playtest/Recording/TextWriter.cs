using System.Globalization;
using System.Text;

namespace Muwbta.Playtest.Recording;

/// <summary>
/// Writes a transcript out as something a person reads.
/// </summary>
/// <remarks>
/// Two views of the same record, because they answer different questions. The per-actor view is
/// "what did this player see", and must contain that player's scrollback and nothing else — a line
/// they could not have seen appearing there would make the whole record untrustworthy. The
/// interleaved view is "what happened, in order", which is the only way to check that one actor's
/// view of another's action lands after the action.
/// </remarks>
public static class TranscriptWriter
{
    /// <summary>
    /// One player's scrollback, as they would have read it.
    /// </summary>
    /// <remarks>
    /// Rendering data is left out — the ASCII grid and the room contents list are panels in the
    /// client, not scrollback — but everything the player could have read is here, including the
    /// apparatus's own <see cref="EntryKind.Meta"/> lines about logins and retries, which are
    /// marked so nobody mistakes them for something the game said.
    /// </remarks>
    public static string ForActor(Transcript transcript, string actor)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var text = new StringBuilder();

        foreach (var entry in transcript.Entries)
        {
            if (!string.Equals(entry.Actor, actor, StringComparison.Ordinal) ||
                entry.Kind == EntryKind.Frame)
            {
                continue;
            }

            text.Append(Stamp(entry.Elapsed)).Append(Render(entry)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Everything, from every actor, in the order it happened.</summary>
    public static string Interleaved(Transcript transcript, IReadOnlyList<string> actors)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(actors);

        var width = actors.Count == 0 ? 8 : Math.Max(8, actors.Max(a => a.Length));
        var text = new StringBuilder();

        foreach (var entry in transcript.Entries)
        {
            if (entry.Kind == EntryKind.Frame)
            {
                continue;
            }

            text.Append(Stamp(entry.Elapsed))
                .Append(entry.Actor.PadRight(width))
                .Append(" │ ")
                .Append(Render(entry).Replace("\n", "\n" + new string(' ', width + 14), StringComparison.Ordinal))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A sigil per kind, so a reviewer can tell the game's voice from the apparatus's at a glance.
    /// </summary>
    private static string Render(TranscriptEntry entry) => entry.Kind switch
    {
        EntryKind.Sent => "> " + entry.Text,
        EntryKind.Text => entry.Text,
        EntryKind.Vitals => "  [" + entry.Text + "]",
        EntryKind.Sys => "  *" + entry.Text,
        EntryKind.Note => "\n── " + entry.Text + "\n",
        EntryKind.Step => "·· " + entry.Text,
        EntryKind.Observation => (entry.Met == true ? "  ✓ " : "  ✗ ") + entry.Text,
        EntryKind.Meta => "  ~ " + entry.Text,
        _ => entry.Text,
    };

    private static string Stamp(TimeSpan elapsed) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}  ");
}
