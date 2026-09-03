using System.Globalization;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Abilities;

/// <summary>How badly wrong an authored ability is.</summary>
public enum AbilityProblemSeverity
{
    /// <summary>Worth saying, never worth refusing a save over.</summary>
    Warning,

    /// <summary>The ability would not work. The builder API refuses it.</summary>
    Error,
}

/// <summary>One thing wrong with an authored ability, or with the set as a whole.</summary>
/// <param name="Key">The ability it is about, or empty for a problem with the set.</param>
public sealed record AbilityProblem(string Key, AbilityProblemSeverity Severity, string Message);

/// <summary>
/// What has to be true of an ability for it to work, checked against authored rows rather than
/// against a list in code.
/// </summary>
/// <remarks>
/// <b>This is the replacement for guardrails that used to be compile-time tests.</b> While
/// <c>AbilityCatalogue</c> was the only source of abilities, twenty-odd invariants could be
/// asserted over a static list and a violation failed the build. Abilities are rows now, so those
/// same invariants have to be checked where rows are made: the builder API refuses an
/// <see cref="AbilityProblemSeverity.Error"/> on save, and the whole set is validated on load so a
/// row that arrived by import or by hand is complained about rather than discovered in play.
///
/// <b>Every check here exists because its absence failed silently.</b> That is the whole character
/// of this class and the reason it is worth its length. An ability naming an effect that does not
/// exist costs its resource, starts its cooldown, and does nothing. An effect reads its parameters
/// by name and skips what it does not recognise, so <c>magnitude</c> where <c>outgoingMultiplier</c>
/// was meant produces a buff that buffs nothing. A "weaken" written as <c>incomingMultiplier</c>
/// below 1.0 reads perfectly and makes its target *harder* to kill — which shipped, to every
/// debuff in the game, and was found by reading the code rather than by anything going wrong.
///
/// The split between error and warning follows §7.4: refuse what is definitely broken and cheap to
/// fix at the point of authoring, and merely report what is a judgement about content shape. A
/// Path with a four-level gap is a design question; an ability that costs nothing is a mistake.
/// </remarks>
public static class AbilityValidator
{
    /// <summary>The highest level a character reaches (PLAN.md §4.7), so unlocks must fit under it.</summary>
    public const int MaxLevel = 50;

    /// <summary>The level by which every Path should have finished unlocking (PLAN.md §4.5).</summary>
    /// <remarks>
    /// <b>Was 20, which is where every Path used to stop.</b> The spacing check below runs only up
    /// to this level, so leaving it at 20 while filling 21–50 would have meant the entire back half
    /// of progression was the one stretch nothing checked — and the check it would have been
    /// missing is the one that catches the failure that filling it exists to fix.
    ///
    /// At 50 it is the level cap, which is the honest answer: a Path is finished unlocking when
    /// there are no more levels to unlock at.
    /// </remarks>
    public const int ProgressionCompleteLevel = MaxLevel;

    /// <summary>The largest gap between unlocks before levelling starts to feel empty.</summary>
    public const int MaxLevelGap = 4;

    /// <summary>
    /// The parameter each effect actually reads. An effect skips what it does not recognise, so a
    /// row missing its one meaningful key is an ability that runs and does nothing.
    /// </summary>
    private static readonly (string EffectKey, string Parameter)[] RequiredParams =
    [
        ("damage.physical", "scalingFactor"),
        ("buff.damage-up", "outgoingMultiplier"),
        ("debuff.weaken", "durationPulses"),
        ("damage.overtime", "tickDamage"),
        ("damage.overtime", "tickIntervalPulses"),
        ("damage.overtime", "durationPulses"),
        ("control.stun", "durationPulses"),
        ("control.root", "durationPulses"),
        ("control.taunt", "leadFraction"),
    ];

    /// <summary>
    /// Effects that read one of several parameters, and are only broken when they have none of them.
    /// </summary>
    /// <remarks>
    /// <c>heal.restore</c> takes a flat <c>baseHeal</c> or a proportional <c>healPercent</c>, and
    /// either alone is a working heal. Listing both in <see cref="RequiredParams"/> would refuse
    /// every heal in the game for not setting the one it deliberately does not use, which is the
    /// opposite of what that table is for.
    /// </remarks>
    private static readonly (string EffectKey, string[] Parameters)[] RequiredOneOf =
    [
        ("heal.restore", ["baseHeal", "healPercent"]),
        ("resource.restore", ["percent", "amount"]),
    ];

    /// <summary>
    /// Everything wrong with one ability, judged on its own. This is what the builder API runs on
    /// save, so it must never need the rest of the table to answer.
    /// </summary>
    public static IReadOnlyList<AbilityProblem> ValidateOne(Ability ability, EffectRegistry effects)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(effects);

        var problems = new List<AbilityProblem>();
        void Error(string message) => problems.Add(new(ability.Key, AbilityProblemSeverity.Error, message));
        void Warn(string message) => problems.Add(new(ability.Key, AbilityProblemSeverity.Warning, message));

        if (string.IsNullOrWhiteSpace(ability.Key))
        {
            Error("An ability needs a key.");
            return problems;
        }

        // The key carries the Path in front of it, and that is load-bearing rather than tidy:
        // AbilityLookup resolves "warden.shield-bash" as well as "shield bash", so a key naming
        // the wrong Path is a name that resolves to somebody else's ability.
        var expectedPrefix = PrefixFor(ability.Path);
        if (!ability.Key.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            Error($"A {ability.Path} ability's key must start with '{expectedPrefix}'.");
        }

        if (string.IsNullOrWhiteSpace(ability.Name))
        {
            Error("An ability needs a display name — it is what a player types.");
        }

        if (ability.CostValue <= 0)
        {
            Error("An ability that costs nothing can be used every time its cooldown allows.");
        }

        if (ability.CooldownPulses < 0)
        {
            Error("A cooldown cannot be negative.");
        }

        // Zero and negative are both refused rather than read as "no timer", because null already
        // means that and a second spelling of it is a value an author could set and then wonder
        // about. Whether the number names a timer anything else is on is a question about the whole
        // set, and is asked in ValidateSet.
        if (ability.CooldownGroup is <= 0)
        {
            Error("A shared timer is a positive number. Leave it empty for an ability that shares none.");
        }

        if (ability.CastTimePulses is < 0)
        {
            Error("A cast time cannot be negative.");
        }

        if (ability.UnlockLevel is < 1 or > MaxLevel)
        {
            Error($"Unlock level must be between 1 and {MaxLevel}. Level 0 is known by everyone.");
        }

        if (ability.Effects.Count == 0)
        {
            Error("An ability with no effects costs its resource and does nothing.");
            return problems;
        }

        // Checked per effect, because an ability is a list of them now. A single bad entry in an
        // otherwise good list is the case worth catching: the ability half-works, which reads as
        // a balance problem rather than as a mistake.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var effect in ability.Effects)
        {
            if (!seen.Add(effect.Key))
            {
                // Both would run, and the second would refresh the first rather than stack with
                // it, so the ability is quietly weaker than it reads.
                Error($"'{effect.Key}' is listed twice.");
                continue;
            }

            if (!effects.Contains(effect.Key))
            {
                // The single most expensive thing to get wrong, because nothing reports it: the
                // cast succeeds, the cost is spent, the cooldown starts, and no effect runs.
                Error($"No effect executor is registered for '{effect.Key}'.");
                continue;
            }

            foreach (var (effectKey, parameter) in RequiredParams)
            {
                if (effect.Key == effectKey && !effect.Params.ContainsKey(parameter))
                {
                    Error($"'{effectKey}' reads '{parameter}', and this ability does not set it.");
                }
            }

            foreach (var (effectKey, parameters) in RequiredOneOf)
            {
                if (effect.Key == effectKey && !parameters.Any(effect.Params.ContainsKey))
                {
                    Error(
                        $"'{effectKey}' reads one of {string.Join(" or ", parameters.Select(p => $"'{p}'"))}, " +
                        "and this ability sets none of them.");
                }
            }

            ValidateDirection(effect, Error);
            ValidateClamps(effect, Error, Warn);
        }

        // One ability points one way. The cast path gathers a single set of targets and treats the
        // ability as harmful if any part of it is, so a list mixing a heal with a damage effect
        // would either burn the people it meant to mend or mend the people it meant to burn -
        // there is no target set that satisfies both. Refused here rather than resolved there,
        // because the resolution would have to be a guess.
        var directions = ability.Effects
            .Select(e => effects.Get(e.Key))
            .OfType<IAbilityEffect>()
            .Select(e => e.IsHarmful)
            .Distinct()
            .Count();

        if (directions > 1)
        {
            Error("An ability cannot be both harmful and helpful — it has one set of targets.");
        }

        return problems;
    }

    /// <summary>
    /// Everything wrong across the whole set — the questions a single row cannot answer, like
    /// whether a Path has anything at level 1.
    /// </summary>
    /// <remarks>
    /// Run on load rather than on save. These are properties of the table, so a save that breaks
    /// one is usually a save that is halfway through a change a builder is still making: refusing
    /// it would make the editor impossible to use, which is the same argument §7.4 makes for
    /// letting the world be temporarily invalid.
    /// </remarks>
    public static IReadOnlyList<AbilityProblem> ValidateSet(
        IReadOnlyCollection<Ability> abilities,
        EffectRegistry effects)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(effects);

        var problems = new List<AbilityProblem>();

        foreach (var ability in abilities)
        {
            problems.AddRange(ValidateOne(ability, effects));
        }

        foreach (var path in Enum.GetValues<CharacterPath>())
        {
            var forPath = abilities
                .Where(a => a.Path == path)
                .OrderBy(a => a.UnlockLevel)
                .ToList();

            if (forPath.Count == 0)
            {
                problems.Add(new("", AbilityProblemSeverity.Warning,
                    $"{path} has no abilities at all."));
                continue;
            }

            if (forPath[0].UnlockLevel != 1)
            {
                problems.Add(new("", AbilityProblemSeverity.Warning,
                    $"{path} has nothing castable at level 1 — a new character of that Path has no ability."));
            }

            var duplicates = forPath
                .GroupBy(a => a.UnlockLevel)
                .Where(g => g.Count() > 1)
                .Select(g => $"level {g.Key} ({string.Join(", ", g.Select(a => a.Key))})");

            foreach (var duplicate in duplicates)
            {
                problems.Add(new("", AbilityProblemSeverity.Warning,
                    $"{path} unlocks two abilities at {duplicate}, so one level gives twice and its neighbours give nothing."));
            }

            var previous = 0;
            foreach (var ability in forPath.Where(a => a.UnlockLevel <= ProgressionCompleteLevel))
            {
                if (ability.UnlockLevel - previous > MaxLevelGap)
                {
                    problems.Add(new("", AbilityProblemSeverity.Warning,
                        $"{path} goes from level {previous} to {ability.UnlockLevel} with nothing new."));
                }

                previous = ability.UnlockLevel;
            }

            if (previous < ProgressionCompleteLevel)
            {
                problems.Add(new("", AbilityProblemSeverity.Warning,
                    $"{path} stops unlocking at level {previous}, short of {ProgressionCompleteLevel}."));
            }

            // A timer with one ability on it shares with nothing, which is the silent-does-nothing
            // shape the rest of this class exists for: the field is set, the editor shows it, and no
            // cast is ever refused because of it. Only answerable across the set, which is why it is
            // here rather than beside the positive-number check in ValidateOne.
            //
            // Grouped within the Path, because the number is only half a timer's identity - see
            // Ability.CooldownGroup. Two Paths using 1 are two timers, not a shared one.
            var lonely = forPath
                .Where(a => a.CooldownGroup is not null and > 0)
                .GroupBy(a => a.CooldownGroup!.Value)
                .Where(g => g.Count() == 1)
                .OrderBy(g => g.Key);

            foreach (var timer in lonely)
            {
                problems.Add(new(timer.Single().Key, AbilityProblemSeverity.Warning,
                    $"{path} timer {timer.Key} has only this ability on it, so it shares with nothing."));
            }

            problems.AddRange(UnsharedRoomControl(path, forPath));
        }

        return problems;
    }

    /// <summary>
    /// Two abilities that take hold of the whole room the same way, and share no timer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reported from play: the Warden's two room taunts could both be fired at once.</b>
    /// Thunderclap takes a lead worth 35% of a mob's health bar and Mass Provocation one worth 50%,
    /// and they do not overwrite each other — <c>Combat.ForceTopHater</c> sets the caster to the
    /// <em>highest</em> hate plus the lead, so the second one lands on top of the first. Cast back
    /// to back they buy a lead of 85% of the bar in a single beat, which is most of a fight's worth
    /// of threat spent in one decision that is not a decision.
    /// </para>
    /// <para>
    /// <b>Scoped to control, and to the room, on purpose.</b> Damage, heals and buffs are meant to
    /// layer — a Hallow stacking three room heals is the Path working — and a single target is one
    /// creature's worth of consequence either way. A control effect over a whole room is the case
    /// where two of them at once removes the thing the abilities exist to make interesting, so it
    /// is the only case flagged. Grouping them is one edit in the builder; the alternative is
    /// finding it in a fight, which is how this one was found.
    /// </para>
    /// <para>
    /// A warning rather than an error, per §7.4: two separate timers may be exactly what an author
    /// wants, and nothing here is broken. It is a judgement about content shape, so it is said and
    /// not enforced.
    /// </para>
    /// </remarks>
    private static IEnumerable<AbilityProblem> UnsharedRoomControl(
        CharacterPath path,
        IReadOnlyList<Ability> forPath)
    {
        var roomControl = forPath
            .Where(a => a.TargetingType == TargetingType.Aoe)
            .SelectMany(a => a.Effects.Select(e => (Ability: a, e.Key)))
            .Where(pair => pair.Key.StartsWith("control.", StringComparison.Ordinal))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var kind in roomControl)
        {
            // Distinct, because one ability carrying the same control twice is a different fault
            // and belongs to whichever check owns duplicate effects.
            var casters = kind
                .Select(pair => pair.Ability)
                .DistinctBy(a => a.Key, StringComparer.Ordinal)
                .ToList();

            if (casters.Count < 2)
            {
                continue;
            }

            // Sharing *a* timer is enough. Which number it is says nothing, and an author who has
            // put two of three on one timer has made a choice about the third worth leaving alone.
            var ungrouped = casters.Where(a => a.CooldownGroup is null or <= 0).ToList();

            if (ungrouped.Count == 0)
            {
                continue;
            }

            var others = string.Join(", ", casters.Select(a => a.Name));

            foreach (var ability in ungrouped)
            {
                yield return new(ability.Key, AbilityProblemSeverity.Warning,
                    $"{path} hits the whole room with {kind.Key} from more than one ability " +
                    $"({others}), and this one is on no shared timer — so they can all be used at " +
                    "once and their effects compound.");
            }
        }
    }

    /// <summary>
    /// A buff must help and a debuff must harm. Both are multipliers around 1.0 and both read
    /// plausibly on the wrong side of it.
    /// </summary>
    private static void ValidateDirection(AbilityEffectSpec ability, Action<string> error)
    {
        switch (ability.Key)
        {
            case "buff.damage-up":
            {
                var outgoing = Number(ability, "outgoingMultiplier");
                if (outgoing is not null and <= 1.0m)
                {
                    error($"A damage buff of {outgoing} makes the caster weaker. Above 1.0 helps.");
                }

                break;
            }

            case "debuff.weaken":
            {
                // The bug this whole class is shaped around. `outgoingMultiplier` scales what the
                // target deals and must go *below* 1.0; `incomingMultiplier` scales what it takes
                // and must go *above*. Every weaken in the game was once written as the latter
                // below 1.0, which made its target 25-45% harder to kill.
                var outgoing = Number(ability, "outgoingMultiplier");
                var incoming = Number(ability, "incomingMultiplier");

                if (outgoing is null && incoming is null)
                {
                    error("A debuff must set outgoingMultiplier or incomingMultiplier, or it does nothing.");
                    break;
                }

                if (outgoing is not null and >= 1.0m)
                {
                    error($"outgoingMultiplier {outgoing} does not weaken. Below 1.0 reduces what the target deals.");
                }

                if (incoming is not null and <= 1.0m)
                {
                    error($"incomingMultiplier {incoming} protects the target. Above 1.0 increases what it takes.");
                }

                break;
            }

            case "damage.overtime":
            {
                var tick = Number(ability, "tickDamage");
                var interval = Number(ability, "tickIntervalPulses");
                var duration = Number(ability, "durationPulses");

                if (tick is not null and <= 0)
                {
                    error($"A wound ticking for {tick} does nothing.");
                }

                if (interval is not null and <= 0)
                {
                    error("A wound needs a positive tick interval.");
                }

                if (interval is > 0 && duration is > 0 && duration < interval)
                {
                    error($"A wound lasting {duration} pulses that ticks every {interval} expires before it ever ticks.");
                }

                break;
            }

            default:
                break;
        }
    }

    /// <summary>
    /// Control effects are clamped by their executors. An authored value past the ceiling is
    /// silently reduced, so the number in the editor stops describing the game.
    /// </summary>
    private static void ValidateClamps(AbilityEffectSpec ability, Action<string> error, Action<string> warn)
    {
        if (!ability.Key.StartsWith("control.", StringComparison.Ordinal))
        {
            return;
        }

        if (Number(ability, "maxStacks") is > 1)
        {
            error("A control effect that stacks chains into a permanent lock.");
        }

        var ceiling = ability.Key switch
        {
            "control.stun" => StunEffect.MaxDurationPulses,
            "control.root" => RootEffect.MaxDurationPulses,
            _ => (long?)null,
        };

        if (ceiling is null)
        {
            return;
        }

        var duration = Number(ability, "durationPulses");

        if (duration is not null and <= 0)
        {
            error($"A control effect lasting {duration} pulses does nothing.");
        }
        else if (duration > ceiling)
        {
            warn($"Lasts {duration} pulses; the executor clamps it to {ceiling}, so the extra is not real.");
        }
    }

    private static decimal? Number(AbilityEffectSpec ability, string key) =>
        ability.Params.TryGetValue(key, out var raw)
        && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string PrefixFor(CharacterPath path) =>
        path.ToString().ToLowerInvariant() + ".";
}
