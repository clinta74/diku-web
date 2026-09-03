namespace Muwbta.Domain.Quests;

/// <summary>
/// The keys the engine looks up in <see cref="Quest.Dialogue"/> (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Named here for the reason <c>MobBehavior</c> names its bag keys, and at the cost of the
/// largest content defect found so far.</b> Dialogue is a free <c>Dictionary&lt;string, string&gt;</c>
/// that passes through the importer, the writer and the applier untouched, so no layer of the round
/// trip is in a position to notice a key nobody reads. All 35 authored quests used
/// <c>offer</c> / <c>progress</c> / <c>complete</c> / <c>already</c> against an engine reading these
/// four — <b>zero overlap</b>. Around 137 lines of authored prose were replaced by four generic
/// templates, in production, with the whole suite green (BUGS.md #6).
/// </para>
/// <para>
/// A literal typed at the lookup site cannot be checked by anything. A constant can: the bundle
/// validator compares authored keys against <see cref="All"/> and refuses one nothing reads, which
/// is the check that would have caught it the day the content landed.
/// </para>
/// </remarks>
public static class QuestDialogue
{
    /// <summary>What the giver says when offering the quest.</summary>
    public const string GiverOffer = "giverOffer";

    /// <summary>What the giver says while it is under way.</summary>
    public const string GiverInProgress = "giverInProgress";

    /// <summary>What the giver says once it is done.</summary>
    public const string GiverComplete = "giverComplete";

    /// <summary>What the turn-in says when the requirement is met.</summary>
    public const string TurninReady = "turninReady";

    /// <summary>
    /// Every key the engine reads. A key outside this set is authored, stored, exported,
    /// re-imported, and never spoken.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        GiverOffer,
        GiverInProgress,
        GiverComplete,
        TurninReady,
    };
}
