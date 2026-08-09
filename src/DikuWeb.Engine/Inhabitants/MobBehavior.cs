namespace DikuWeb.Engine.Inhabitants;

/// <summary>
/// What a mob does when nobody is fighting it, and whether it can be fought at all.
/// </summary>
/// <remarks>
/// Stored as the <c>type</c> key of the behavior bag rather than as a column, because the bag is
/// the extensibility point PLAN.md §4.8 chose and a disposition is exactly the sort of thing a
/// builder toggles. The lowercase names are the persisted strings - renaming one is a content
/// migration, not a rename.
/// </remarks>
public enum MobDisposition
{
    /// <summary>Minds its own business, but fights back. The default for anything unlabelled.</summary>
    Passive,

    /// <summary>Attacks any player who enters the room.</summary>
    Aggressive,

    /// <summary>
    /// A non-combatant: never attacks, and cannot be attacked. Quest givers, turn-ins, and
    /// shopkeepers want this - a killable quest giver stands a player's progress up against a
    /// respawn timer, and PLAN.md §7.4 is explicit that progress must not be strandable.
    /// </summary>
    Npc,
}

/// <summary>
/// The behavior bag's vocabulary: which keys mean what, and how to read them.
/// </summary>
/// <remarks>
/// The keys are named here rather than spelled inline at each call site, because
/// <c>"shopkeeper"</c> typed in two places is one typo away from a shop that exists to the
/// builder and not to the game. Values go through <see cref="JsonBag"/> - see its remarks for
/// why reading them directly does not work.
/// </remarks>
public static class MobBehavior
{
    /// <summary>The behavior key holding a <see cref="MobDisposition"/>.</summary>
    public const string TypeKey = "type";

    /// <summary>The behavior key holding the idle emote list.</summary>
    public const string EmotesKey = "emotes";

    /// <summary>The behavior key marking a mob as a shopkeeper.</summary>
    public const string ShopkeeperKey = "shopkeeper";

    /// <summary>The behavior key holding the item template keys a shopkeeper sells.</summary>
    public const string SellsKey = "sells";

    /// <summary>The behavior key letting a mob wander out of the zone it spawned in.</summary>
    public const string RoamsKey = "roams";

    /// <summary>The persisted string for a disposition, for writers and for the builder API.</summary>
    public static string NameOf(MobDisposition disposition) => disposition switch
    {
        MobDisposition.Aggressive => "aggressive",
        MobDisposition.Npc => "npc",
        _ => "passive",
    };

    /// <summary>
    /// The disposition this bag declares. Anything unrecognised reads as
    /// <see cref="MobDisposition.Passive"/>: an unknown word must not silently make a mob
    /// hostile, and must not make it invulnerable either.
    /// </summary>
    public static MobDisposition DispositionOf(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Text(behavior, TypeKey)?.ToLowerInvariant() switch
        {
            "aggressive" => MobDisposition.Aggressive,
            "npc" => MobDisposition.Npc,
            _ => MobDisposition.Passive,
        };

    /// <summary>True when this mob attacks players on sight.</summary>
    public static bool IsAggressive(IReadOnlyDictionary<string, object>? behavior) =>
        DispositionOf(behavior) == MobDisposition.Aggressive;

    /// <summary>
    /// True when this mob is a non-combatant, and so may neither attack nor be attacked.
    /// </summary>
    public static bool IsNonCombatant(IReadOnlyDictionary<string, object>? behavior) =>
        DispositionOf(behavior) == MobDisposition.Npc;

    /// <summary>True when this mob keeps a shop.</summary>
    public static bool IsShopkeeper(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Boolean(behavior, ShopkeeperKey);

    /// <summary>
    /// True when this mob may wander out of the zone it spawned in.
    /// </summary>
    /// <remarks>
    /// <b>False by default, which confines it.</b> A zone is the unit difficulty is authored in
    /// (§4.4) — its multipliers are what make a rat in the starting meadow different from a rat in
    /// the crypt — so a mob strolling across a zone border carries numbers that belong to
    /// somewhere else. Fencing by geography meant flagging every border room <c>noMob</c> and
    /// remembering to do it again whenever a builder dug a new exit; fencing by origin is a
    /// property of the mob, so it cannot be forgotten.
    ///
    /// This does not change how <c>noMob</c> works. That flag says "not into this room"; this says
    /// "not out of that zone", and a mob has to satisfy both.
    /// </remarks>
    public static bool Roams(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Boolean(behavior, RoamsKey);

    /// <summary>The idle emotes this mob cycles through. Empty when it has none.</summary>
    public static IReadOnlyList<string> EmotesOf(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Strings(behavior, EmotesKey);

    /// <summary>The item template keys this shopkeeper offers. Empty when it stocks nothing.</summary>
    public static IReadOnlyList<string> SellsOf(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Strings(behavior, SellsKey);
}
