using DikuWeb.Server.Building;

namespace DikuWeb.Server.Assist;

/// <summary>
/// What is wrong with a draft that the grammar could not prevent.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because a constrained sampler cannot decline.</b> The schema guarantees the
/// output parses, that directions are directions, and that an exit leads somewhere real. It cannot
/// guarantee the draft is sensible, and the first real generation proved the point by making itself
/// a bind point (see <c>AssistSchema.NotGenerated</c>). Everything a per-room check can catch is
/// caught here, before a builder is shown the draft rather than after they save it.
/// </para>
/// <para>
/// <b>Warnings, never refusals.</b> The draft still goes back with them attached. A builder reading
/// "this names two exits north" can fix it in a second; a builder told the assist failed learns
/// nothing and has to ask again, which costs three minutes of somebody's model.
/// </para>
/// <para>
/// Deliberately not <c>BundleValidator</c>. That takes a whole bundle and asks graph questions -
/// reciprocity, connectivity, reachability - which a single room cannot answer and which the normal
/// save path already asks when the room joins the world. Duplicating a slice of it here would be a
/// second set of rules to keep in step.
/// </para>
/// </remarks>
public static class RoomDraftReview
{
    /// <summary>Phrases that mean the prose is describing the exits the engine already lists.</summary>
    /// <remarks>
    /// Crude on purpose - it is a nudge to reread, not a parser. The prompt asks for no exits and
    /// the first generation wrote "a staircase rises from the gate" anyway, which is the kind of
    /// thing a builder scanning quickly accepts and a room then contradicts.
    /// </remarks>
    private static readonly string[] ExitWords =
    [
        " leads ", " leads.", "to the north", "to the south", "to the east", "to the west",
        "staircase", "stairs lead", "path leads", "door opens onto",
    ];

    /// <summary>Everything questionable about this draft, in the order a reader would want it.</summary>
    public static IReadOnlyList<string> Review(RoomDraft draft, IReadOnlyCollection<string> destinations)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(destinations);

        var warnings = new List<string>();

        // The grammar caps the array at six and enumerates the directions, and still cannot say
        // "each at most once" - JSON Schema has no uniqueness over a field. So two norths is a
        // legal completion, and the second one silently wins on import.
        foreach (var group in draft.Exits
            .GroupBy(e => e.Direction, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            warnings.Add($"names {group.Key} {group.Count()} times; a room has one exit per direction");
        }

        foreach (var exit in draft.Exits.Where(e => !destinations.Contains(e.To)))
        {
            // Should be unreachable - `to` is an enum of exactly these - so this firing means the
            // constraint did not hold, which is worth saying out loud rather than tolerating.
            warnings.Add($"leads {exit.Direction} to '{exit.To}', which was not offered to it");
        }

        if (draft.Title.Length > AssistSchema.TitleMaxLength)
        {
            warnings.Add($"has a {draft.Title.Length}-character title, over the "
                + $"{AssistSchema.TitleMaxLength} the schema set");
        }

        if (draft.Description.Length > AssistSchema.DescriptionMaxLength)
        {
            warnings.Add($"has a {draft.Description.Length}-character description, over the "
                + $"{AssistSchema.DescriptionMaxLength} the schema set");
        }

        if (ExitWords.Any(w => draft.Description.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("describes a way out; the engine writes the exit line, so prose that "
                + "names one can contradict it");
        }

        return warnings;
    }
}
