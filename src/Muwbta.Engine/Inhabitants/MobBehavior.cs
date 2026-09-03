namespace Muwbta.Engine.Inhabitants;

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

    /// <summary>The behavior key holding what this mob says when talked to.</summary>
    /// <remarks>
    /// A list, picked from at random, exactly like <see cref="EmotesKey"/> — the difference
    /// being that an emote is unprompted and a greeting is an answer. Both are lists for the
    /// same reason: one line repeated is how a populated world starts reading thin.
    /// </remarks>
    public const string GreetingKey = "greeting";

    /// <summary>The behavior key marking a mob as a shopkeeper.</summary>
    public const string ShopkeeperKey = "shopkeeper";

    /// <summary>The behavior key holding the item template keys a shopkeeper sells.</summary>
    public const string SellsKey = "sells";

    /// <summary>The behavior key holding how far over base value a shopkeeper prices its stock.</summary>
    public const string MarkupKey = "markup";

    /// <summary>The behavior key letting a mob wander out of the zone it spawned in.</summary>
    public const string RoamsKey = "roams";

    /// <summary>The behavior key saying whether this mob wanders between rooms at all.</summary>
    public const string WandersKey = "wanders";

    /// <summary>
    /// Every key this class reads out of a behavior bag. A key outside this set reaches nothing.
    /// </summary>
    /// <remarks>
    /// <b>Top-level bag keys only.</b> <see cref="MobEmote"/>'s three — <c>text</c>,
    /// <c>minSeconds</c>, <c>maxSeconds</c> — live one level down inside an entry of
    /// <see cref="EmotesKey"/> and are not valid here, and neither are the <em>values</em> of
    /// <see cref="TypeKey"/>. Both distinctions were lost by the regex that used to harvest this
    /// list out of the source file, which swept up every lowercase literal in it and so quietly
    /// accepted <c>aggressive</c> and <c>minSeconds</c> as behavior keys of their own.
    /// </remarks>
    public static IReadOnlySet<string> KnownKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        TypeKey,
        EmotesKey,
        GreetingKey,
        ShopkeeperKey,
        SellsKey,
        MarkupKey,
        RoamsKey,
        WandersKey,
    };

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

    /// <summary>
    /// True when this mob moves between rooms of its own accord. <b>False by default</b>, so a
    /// template that says nothing stays where it is put.
    /// </summary>
    /// <remarks>
    /// <b>The default is standing still, and that is the change.</b> Wandering used to be the
    /// behaviour of anything whose spawner did not say otherwise, which is backwards twice over:
    /// most authored mobs are shopkeepers, quest givers, and guards that belong somewhere
    /// specific, and the ones that should roam are the minority worth naming. It also meant the
    /// decision lived on the spawner, so the same shopkeeper placed by two spawners could wander
    /// from one and not the other — the mob is the thing with an opinion about whether it moves.
    ///
    /// Absence resolving to the quieter behaviour is the same rule room flags follow (§4.10): a
    /// key that was mistyped, or written by an older builder, must fail toward the harmless
    /// answer. A mob that should have wandered and does not is a dull room; a quest giver that
    /// wanders off is a player unable to finish a chain.
    ///
    /// This is the mob's own default. <see cref="Domain.Spawning.Spawner.Wanders"/> overrides it
    /// per placement and has the last word.
    /// </remarks>
    public static bool Wanders(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Boolean(behavior, WandersKey);

    /// <summary>The idle emote text this mob cycles through. Empty when it has none.</summary>
    /// <remarks>
    /// Text only. <see cref="EmoteScheduleOf"/> is what the AI reads — this stays for callers that
    /// want to know <em>whether</em> a mob says anything without caring how often.
    /// </remarks>
    public static IReadOnlyList<string> EmotesOf(IReadOnlyDictionary<string, object>? behavior) =>
        [.. EmoteScheduleOf(behavior).Select(e => e.Text)];

    /// <summary>What this mob says when talked to. Empty when it has nothing authored.</summary>
    /// <remarks>
    /// Bare strings rather than the timed rows emotes accept: a greeting has no cadence,
    /// because it happens when somebody speaks to it rather than on a schedule.
    /// </remarks>
    public static IReadOnlyList<string> GreetingsOf(
        IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Strings(behavior, GreetingKey);

    /// <summary>The keys a timed emote row is authored with.</summary>
    public const string EmoteTextKey = "text";

    /// <inheritdoc cref="EmoteTextKey"/>
    public const string EmoteMinSecondsKey = "minSeconds";

    /// <inheritdoc cref="EmoteTextKey"/>
    public const string EmoteMaxSecondsKey = "maxSeconds";

    /// <summary>
    /// The idle emotes this mob cycles through, each with its own cadence.
    /// </summary>
    /// <remarks>
    /// <b>Two authored shapes, both valid.</b> A bare string is a line with no opinion about
    /// timing and takes the default range; a row carrying <c>text</c>, <c>minSeconds</c>, and
    /// <c>maxSeconds</c> says how often it should be heard. The list may mix them, because the bag
    /// is schemaless (§4.8) and every emote written before timing existed is a bare string — so
    /// accepting only rows would have silenced all of them.
    ///
    /// A row with no <c>text</c> is dropped rather than emitted blank: that is a half-filled
    /// builder row, and "a rat ." is worse than silence.
    /// </remarks>
    public static IReadOnlyList<MobEmote> EmoteScheduleOf(IReadOnlyDictionary<string, object>? behavior)
    {
        var emotes = new List<MobEmote>();

        foreach (var entry in JsonBag.Items(behavior, EmotesKey))
        {
            if (JsonBag.AsBag(entry) is { } row)
            {
                if (JsonBag.Text(row, EmoteTextKey) is not { } text)
                {
                    continue;
                }

                var min = JsonBag.Int32(row, EmoteMinSecondsKey, MobEmote.DefaultMinSeconds);
                var max = JsonBag.Int32(row, EmoteMaxSecondsKey, MobEmote.DefaultMaxSeconds);

                emotes.Add(MobEmote.FromSeconds(text, min, max));
                continue;
            }

            var bare = Convert.ToString(entry, System.Globalization.CultureInfo.InvariantCulture)?.Trim();

            if (!string.IsNullOrEmpty(bare))
            {
                emotes.Add(MobEmote.FromSeconds(
                    bare, MobEmote.DefaultMinSeconds, MobEmote.DefaultMaxSeconds));
            }
        }

        return emotes;
    }

    /// <summary>The item template keys this shopkeeper offers. Empty when it stocks nothing.</summary>
    public static IReadOnlyList<string> SellsOf(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Strings(behavior, SellsKey);

    /// <summary>
    /// How far over base value this shopkeeper prices its stock, where <c>0.1</c> means 1.1x
    /// (PLAN.md §4.13). Zero when it charges base price, which is what absence means.
    /// </summary>
    /// <remarks>
    /// The arithmetic is <see cref="Domain.Items.ShopPricing.Price"/>; this only says what the mob
    /// asked for. Kept apart because the bag is an Engine concern and the rounding rule is not -
    /// what a shop charges should be answerable without a mob to hand.
    /// </remarks>
    public static decimal MarkupOf(IReadOnlyDictionary<string, object>? behavior) =>
        JsonBag.Decimal(behavior, MarkupKey);
}
