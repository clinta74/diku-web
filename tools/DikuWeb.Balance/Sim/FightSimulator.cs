using DikuWeb.Balance.Content;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Balance.Sim;

/// <summary>
/// One character against one mob, pulse by pulse, until somebody stops standing.
/// </summary>
/// <remarks>
/// <para>
/// <b>A simulation rather than a formula, because the question is not answerable in closed form.</b>
/// Sustained damage per second can be worked out on paper for a single ability; what cannot is what
/// happens when eleven of them share a resource bar, half leave wounds that tick under buffs read at
/// tick time, and the fight ends when the target's health runs out rather than after a fixed
/// window. Those interactions are the whole question — whether ability damage still matters at level
/// 50 depends on how much of it a real fight has room for.
/// </para>
/// <para>
/// <b>Every number comes from the Domain.</b> <c>DamageCalculator</c> rolls the swings,
/// <c>EquipmentResolver</c> reads the gear, the real <c>IAbilityEffect</c> executors apply the
/// effects, <c>DamageMultipliers</c> composes the buffs, <c>VitalCalculator</c> and
/// <c>PathGrowth</c> build the character. The loop below decides only <em>when</em> things happen,
/// never how much they are worth. A harness that reimplemented the damage formula would be
/// measuring the harness.
/// </para>
/// <para>
/// <b>What is deliberately not modelled:</b> movement, fleeing, positioning, other players, and
/// death recovery. All four would change how a fight is played and none change what a swing or a
/// cast is worth, which is what this is for.
/// </para>
/// </remarks>
public sealed class FightSimulator
{
    /// <summary>A pulse is 250 ms (PLAN.md §2.1).</summary>
    public const int PulsesPerSecond = 4;

    /// <summary>The 60-second cadence the server regenerates vitals on.</summary>
    private const int RegenIntervalSeconds = 60;

    /// <inheritdoc cref="RegenIntervalSeconds"/>
    private const int RegenIntervalPulses = RegenIntervalSeconds * PulsesPerSecond;

    /// <summary>
    /// Below this share of health, healing outranks damage in the rotation.
    /// </summary>
    /// <remarks>
    /// A threshold rather than a reaction to incoming damage, because the alternative is modelling
    /// how well a player reads a fight — which is not a property of the content and would put the
    /// harness's opinion into the result. Two fifths is low enough that a Path with no heal is not
    /// penalised for having none, and high enough that one with a heal actually presses it.
    /// </remarks>
    private const double HealBelow = 0.4;

    /// <summary>
    /// A wound or buff with more than this share of its duration left is not re-applied.
    /// </summary>
    /// <remarks>
    /// Clipping a bleed to refresh it wastes the cast and the resource, and no player does it on
    /// purpose. A third leaves room to re-open one that is nearly out without pretending to
    /// frame-perfect timing.
    /// </remarks>
    private const double RefreshBelow = 0.33;

    /// <summary>
    /// Below this share of a bar, a restore of that bar is worth the cast.
    /// </summary>
    /// <remarks>
    /// Not lower, because the ability has a cast time and a five-minute cooldown - waiting until
    /// the bar is empty wastes the seconds spent casting while unable to spend anything. Not
    /// higher, because a restore worth half a bar poured into a bar that is two thirds full is
    /// half of it thrown away. Just under half leaves room for the largest restore in the game to
    /// land whole.
    /// </remarks>
    private const double RestoreBelow = 0.45;

    /// <summary>
    /// The least time between two casts.
    /// </summary>
    /// <remarks>
    /// <b>There is no global cooldown in this game, and modelling one anyway is the honest
    /// choice.</b> The loop would happily accept a cast every pulse, so an unthrottled rotation
    /// opens a fight by dumping every ability that is off cooldown inside two seconds - which is
    /// four commands a second, something no player types and only a scripted client could do. Left
    /// in, it made a level 50 Temper kill in 1.3 seconds and reported abilities as 99% of their
    /// damage.
    ///
    /// <see cref="AttackTiming.MinDelayPulses"/> rather than a number invented here: one second is
    /// already the game's declared floor on how often a combatant may act, and a cast is an act.
    /// </remarks>
    private const int CastIntervalPulses = AttackTiming.MinDelayPulses;

    private readonly EffectRegistry _effects = new();

    /// <summary>
    /// Runs one fight and reports what it cost.
    /// </summary>
    /// <param name="content">The loaded world.</param>
    /// <param name="path">The Path being measured.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="loadout">What they are wearing.</param>
    /// <param name="encounter">The mob, already scaled by its zone.</param>
    /// <param name="seed">Seeded so a surprising result can be re-run and watched.</param>
    /// <param name="capSeconds">
    /// When to give up. A fight that reaches this is reported as a
    /// <see cref="FightOutcome.Stalemate"/> rather than discarded.
    /// </param>
    /// <param name="useAbilities">
    /// False runs the same fight with the ability bar switched off. Two runs of one fight, differing
    /// only here, is the cleanest measure of what the bar is worth — better than the damage split,
    /// which cannot see that a slower kill also means more incoming damage.
    /// </param>
    /// <param name="regenScale">
    /// Multiplies what a regeneration tick returns.
    ///
    /// <b>An experiment knob, not a rule.</b> <c>RegenCalculator</c> returns 2% of a maximum per
    /// <em>60-second</em> tick, which was set when a fight was six seconds long and is now the
    /// binding constraint on fights that run past a minute. This exists so the size of a proposed
    /// change can be measured before anybody makes one - the harness has no business quietly
    /// running on a rate the game does not use, so it defaults to 1.0 and the report prints it.
    /// </param>
    public FightResult Run(
        ContentSet content,
        CharacterPath path,
        int level,
        Loadout loadout,
        Encounter encounter,
        int seed,
        int capSeconds = 600,
        bool useAbilities = true,
        double regenScale = 1.0,
        int regenSeconds = 60)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(encounter);

        var random = new SeededRandomSource(seed);

        var character = BuildCharacter(path, level);
        var mob = encounter.ToMob();

        // The same strings the executors stamp onto an effect they build. Asked of
        // EffectSource rather than spelled out, because a prefix that drifted from the
        // executors' would make every lookup here miss in silence.
        var playerSource = EffectSource.Of(character);
        var mobSource = EffectSource.Of(mob);

        var playerEffects = new EffectBag();
        var mobEffects = new EffectBag();

        var equipped = loadout.Equipped;
        var offHandShare = AbilityProgression.OffHandDamageShare(path, level);

        var mainHandDelay = loadout.MainHandDelayPulses(content);
        var offHandDelay = loadout.OffHandDelayPulses(content);
        var hasOffHand = loadout.HasOffHandWeapon(content) && offHandShare > 0m;

        var known = useAbilities ? KnownAbilities(content, path, level) : [];
        var lastCast = new Dictionary<string, long>(StringComparer.Ordinal);

        var mobAttacks = ResolveAttacks(encounter);
        var mobAttackerStats = DamageCalculator.StatsFrom(mob);

        // Timers start ready, so both sides open the fight by swinging. Staggering them would be
        // inventing an opening advantage the game does not grant either side.
        var nextMainHand = 0L;
        var nextOffHand = 0L;
        var mobNext = new long[mobAttacks.Count];
        var busyUntil = 0L;

        int weaponDamage = 0, abilityDamage = 0, woundDamage = 0, taken = 0;
        int swings = 0, casts = 0, starved = 0;
        int focusSpent = 0, staminaSpent = 0;

        var capPulses = (long)capSeconds * PulsesPerSecond;
        long pulse = 0;

        for (; pulse < capPulses; pulse++)
        {
            playerEffects.Expire(pulse, character.Vitals);
            mobEffects.Expire(pulse, mob.Vitals);

            // --- wounds tick first, so a fight can end to one --------------------------------
            woundDamage += TickWounds(mobEffects, mob.Vitals, playerEffects, mobEffects, pulse);

            if (mob.Vitals.Health <= 0)
            {
                break;
            }

            taken += TickWounds(playerEffects, character.Vitals, mobEffects, playerEffects, pulse);

            if (character.Vitals.Health <= 0)
            {
                break;
            }

            // --- the player acts --------------------------------------------------------------
            if (!playerEffects.Incapacitated && pulse >= busyUntil)
            {
                var choice = Choose(known, character, lastCast, pulse, mobEffects, playerEffects, playerSource);

                if (choice.Starved)
                {
                    starved++;
                }

                if (choice.Ability is { } ability)
                {
                    var before = mob.Vitals.Health;
                    var healthBefore = character.Vitals.Health;

                    switch (ability.CostType)
                    {
                        case CostType.Focus:
                            focusSpent += Math.Min(character.Vitals.Focus, ability.CostValue);
                            break;
                        case CostType.Stamina:
                            staminaSpent += Math.Min(character.Vitals.Stamina, ability.CostValue);
                            break;
                    }

                    Cast(ability, character, mob, random, pulse,
                        playerEffects, mobEffects, playerSource, lastCast);

                    // Health before and after, rather than asking the executor how much it dealt -
                    // the rule AbilitySystem uses, for the reason it gives: the executors return
                    // void and each computes damage its own way, so the wound is the one measure
                    // true of all of them.
                    var dealt = before - mob.Vitals.Health;

                    if (dealt > 0)
                    {
                        var scaled = DamageMultipliers.Apply(
                            dealt,
                            DamageMultipliers.Between(playerEffects.All, mobEffects.All));

                        mob.Vitals.Health = Math.Max(0, before - scaled);
                        abilityDamage += scaled;
                    }
                    else
                    {
                        // A heal, a buff, a taunt. Self-damage from a Health-cost ability is
                        // already off the bar and is not damage the player dealt.
                        _ = healthBefore;
                    }

                    casts++;
                    busyUntil = pulse + Math.Max(CastIntervalPulses, ability.CastTimePulses ?? 0);

                    if (mob.Vitals.Health <= 0)
                    {
                        break;
                    }
                }
            }

            // --- the player swings ------------------------------------------------------------
            if (pulse >= nextMainHand)
            {
                weaponDamage += Swing(
                    EquipmentResolver.ResolveAttackerStatsForHand(
                        level, character.Attributes.MightModifier, equipped, ItemSlot.MainHand, 1m),
                    DefenderFor(mob, mobEffects),
                    mob.Vitals, playerEffects, mobEffects, random);

                swings++;
                nextMainHand = pulse + mainHandDelay;

                if (mob.Vitals.Health <= 0)
                {
                    break;
                }
            }

            if (hasOffHand && pulse >= nextOffHand)
            {
                weaponDamage += Swing(
                    EquipmentResolver.ResolveAttackerStatsForHand(
                        level, character.Attributes.MightModifier, equipped, ItemSlot.OffHand, offHandShare),
                    DefenderFor(mob, mobEffects),
                    mob.Vitals, playerEffects, mobEffects, random);

                swings++;
                nextOffHand = pulse + offHandDelay;

                if (mob.Vitals.Health <= 0)
                {
                    break;
                }
            }

            // --- the mob swings ---------------------------------------------------------------
            if (!mobEffects.Incapacitated)
            {
                for (var i = 0; i < mobAttacks.Count; i++)
                {
                    if (pulse < mobNext[i])
                    {
                        continue;
                    }

                    taken += MobSwing(
                        mobAttacks[i], mobAttackerStats,
                        PlayerDefender(level, character, equipped, playerEffects),
                        character, mob, random, pulse,
                        mobEffects, playerEffects, mobSource);

                    mobNext[i] = pulse + mobAttacks[i].DelayPulses;

                    if (character.Vitals.Health <= 0)
                    {
                        break;
                    }
                }
            }

            if (character.Vitals.Health <= 0)
            {
                break;
            }

            // --- the minute tick ---------------------------------------------------------------
            if (pulse > 0 && pulse % Math.Max(1, regenSeconds * PulsesPerSecond) == 0)
            {
                // Scaled to the interval, so shortening it redistributes the same recovery rather
                // than multiplying it: ticking twice as often for the same amount each time would
                // be testing two changes at once and reporting them as one.
                var share = regenScale * regenSeconds / (double)RegenIntervalSeconds;

                var (rHealth, rFocus, rStamina) = RegenCalculator.Calculate(
                    CharacterRestState.Stand,
                    character.Vitals,
                    character.Attributes.VitalityModifier,
                    path);

                character.Vitals.Health = Math.Min(
                    character.Vitals.HealthMax,
                    character.Vitals.Health + (int)(rHealth * share));
                character.Vitals.Focus = Math.Min(
                    character.Vitals.FocusMax,
                    character.Vitals.Focus + (int)(rFocus * share));
                character.Vitals.Stamina = Math.Min(
                    character.Vitals.StaminaMax,
                    character.Vitals.Stamina + (int)(rStamina * share));

                // Mobs regenerate too (PLAN.md §4.6), which is exactly what turns a fight the
                // player cannot quite win into one that never ends.
                mob.Vitals.Health = Math.Min(
                    mob.Vitals.HealthMax,
                    mob.Vitals.Health + RegenCalculator.HealthFor(
                        CharacterRestState.Stand, mob.Vitals, 0));
            }
        }

        var outcome = mob.Vitals.Health <= 0
            ? FightOutcome.Won
            : character.Vitals.Health <= 0
                ? FightOutcome.Lost
                : FightOutcome.Stalemate;

        return new FightResult(
            Outcome: outcome,
            Seconds: (double)pulse / PulsesPerSecond,
            WeaponDamage: weaponDamage,
            AbilityDamage: abilityDamage,
            WoundDamage: woundDamage,
            DamageTaken: taken,
            HealthRemaining: Math.Max(0, character.Vitals.Health),
            HealthMax: character.Vitals.HealthMax,
            Swings: swings,
            Casts: casts,
            StarvedPulses: starved,
            FocusSpent: focusSpent,
            FocusMax: character.Vitals.FocusMax,
            StaminaSpent: staminaSpent,
            StaminaMax: character.Vitals.StaminaMax);
    }

    // ---------------------------------------------------------------------------------------
    // Building the combatants
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A character of this Path at this level, grown the way levelling grows one.
    /// </summary>
    /// <remarks>
    /// Stepped through <see cref="StatGrowth.ApplyTo"/> once per level rather than multiplied out,
    /// because the growth clamps at <c>AttributeSet.MaxValue</c> on every step. Multiplying and
    /// clamping once would give a level 50 Temper the Might of a level 50 Temper who had never hit
    /// the cap - which is the same number here, and would stop being so the moment anything
    /// non-linear entered the curve.
    /// </remarks>
    public static Character BuildCharacter(CharacterPath path, int level)
    {
        var attributes = AttributeSet.Baseline;
        var growth = PathGrowth.For(path);

        for (var i = 1; i < level; i++)
        {
            growth.ApplyTo(ref attributes);
        }

        var vitals = Vitals.StartingFor(path);
        VitalCalculator.RecalculateMaxima(path, level, attributes, vitals);

        vitals.Health = vitals.HealthMax;
        vitals.Focus = vitals.FocusMax;
        vitals.Stamina = vitals.StaminaMax;

        return new Character
        {
            AccountId = Guid.Empty,
            Name = $"{path} {level}",
            Path = path,
            Level = level,
            Attributes = attributes,
            Vitals = vitals,
            RoomKey = RoomKey.Create("balance", "harness", "ring"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Every ability this Path has by this level, from the loaded table.</summary>
    private static List<Ability> KnownAbilities(ContentSet content, CharacterPath path, int level) =>
        [.. AbilityProgression
            .GetKnownAbilitiesForLevel(content.Abilities.Values, path, level)
            .Select(key => content.Abilities[key])];

    /// <summary>
    /// The mob's attacks, normalised the way <c>MobAttackResolver</c> normalises them.
    /// </summary>
    /// <remarks>
    /// Reproduced rather than called because that method takes a Domain <c>MobTemplate</c> and what
    /// is in hand is a bundle record. The two clamps it applies are the load-bearing part - a
    /// template written before the speed floor existed can otherwise swing four times too fast -
    /// and both come from <see cref="AttackTiming"/> here rather than from constants retyped.
    /// </remarks>
    private static List<MobAttack> ResolveAttacks(Encounter encounter)
    {
        if (encounter.Attacks is not { Count: > 0 })
        {
            return [new MobAttack { DelayPulses = AttackTiming.DefaultDelayPulses }];
        }

        return
        [
            .. encounter.Attacks
                .Where(a => a is not null)
                .Select(a => new MobAttack
                {
                    Verb = AttackTiming.VerbOr(a.Verb),
                    DelayPulses = AttackTiming.Clamp(a.DelayPulses),
                    DamageMultiplier = a.DamageMultiplier is > 0m ? a.DamageMultiplier : null,
                    EffectKey = string.IsNullOrWhiteSpace(a.EffectKey) ? null : a.EffectKey.Trim(),
                    EffectParams = a.EffectParams,
                }),
        ];
    }

    // ---------------------------------------------------------------------------------------
    // Defence, with effects folded in
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Mirrors <c>CombatSystem.WithEffects</c>: guard effects ride beside the armour rating rather
    /// than being folded into it, and <see cref="DamageCalculator"/> clamps the sum once.
    /// </summary>
    private static DefenderStats WithEffects(DefenderStats stats, EffectBag effects)
    {
        var defense = effects.DefenseRatingDelta;
        var mitigation = effects.MitigationDelta;

        if (defense == 0 && mitigation == 0m)
        {
            return stats;
        }

        return stats with
        {
            DefenseRating = Math.Max(0, stats.DefenseRating + defense),
            MitigationDelta = mitigation,
        };
    }

    private static DefenderStats DefenderFor(Mob mob, EffectBag effects) =>
        WithEffects(DamageCalculator.DefenderStatsFrom(mob), effects);

    private static DefenderStats PlayerDefender(
        int level, Character character, IReadOnlyList<ItemInstance> equipped, EffectBag effects) =>
        WithEffects(
            EquipmentResolver.ResolveDefenderStats(
                level, character.Attributes.AgilityModifier, equipped),
            effects);

    // ---------------------------------------------------------------------------------------
    // Swings
    // ---------------------------------------------------------------------------------------

    /// <summary>One weapon attack, rolled and applied. Returns what actually landed.</summary>
    private static int Swing(
        AttackerStats attacker,
        DefenderStats defender,
        Vitals targetVitals,
        EffectBag attackerEffects,
        EffectBag targetEffects,
        IRandomSource random)
    {
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        if (!result.Hit)
        {
            return 0;
        }

        var damage = DamageMultipliers.Apply(
            result.DamageDealt,
            DamageMultipliers.Between(attackerEffects.All, targetEffects.All));

        targetVitals.Health = Math.Max(0, targetVitals.Health - damage);

        return damage;
    }

    /// <summary>One mob attack, including whatever rider it carries on a landed hit.</summary>
    private int MobSwing(
        MobAttack attack,
        AttackerStats baseStats,
        DefenderStats defender,
        Character target,
        Mob mob,
        IRandomSource random,
        long pulse,
        EffectBag mobEffects,
        EffectBag playerEffects,
        string mobSource)
    {
        // The multiplier scales this attack against the mob's own resolved damage, so a rake can
        // hit harder than a bite without either declaring dice of its own.
        var stats = attack.DamageMultiplier is { } multiplier and > 0m
            ? baseStats with
            {
                MinDamage = (int)Math.Ceiling(baseStats.MinDamage * multiplier),
                MaxDamage = Math.Max(
                    (int)Math.Ceiling(baseStats.MinDamage * multiplier),
                    (int)Math.Ceiling(baseStats.MaxDamage * multiplier)),
            }
            : baseStats;

        var before = target.Vitals.Health;
        var dealt = Swing(stats, defender, target.Vitals, mobEffects, playerEffects, random);

        if (dealt <= 0 || attack.EffectKey is null)
        {
            return dealt;
        }

        // A rider lands only on a hit, and leaves its level at zero - which is what makes two mob
        // riders compare equal and keep the stacking behaviour they have always had.
        if (_effects.Get(attack.EffectKey) is IBuffEffect buff)
        {
            var parameters = attack.EffectParams ?? [];
            var effect = buff.CreateActiveEffect(mob, target, parameters, pulse);
            playerEffects.Apply(effect, target.Vitals);
        }

        _ = before;
        return dealt;
    }

    // ---------------------------------------------------------------------------------------
    // Wounds
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Ticks every wound on one combatant, under the buffs in force at this pulse.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>CombatSystem.TickDamageOverTime</c>, including the two rules that are easy to
    /// miss: a tick falling on the expiry pulse never happens, and the multipliers are read
    /// <em>now</em> rather than frozen when the wound was opened.
    /// </remarks>
    private static int TickWounds(
        EffectBag bearer,
        Vitals bearerVitals,
        EffectBag sourceEffects,
        EffectBag bearerEffects,
        long pulse)
    {
        var total = 0;

        foreach (var effect in bearer.All.ToList())
        {
            if (!effect.Ticks || effect.NextTickPulse > pulse || effect.ExpiresAtPulse <= pulse)
            {
                continue;
            }

            effect.NextTickPulse = pulse + effect.TickIntervalPulses;

            var damage = DamageMultipliers.Apply(
                effect.TickDamage * Math.Max(1, effect.Stacks),
                DamageMultipliers.Between(sourceEffects.All, bearerEffects.All));

            bearerVitals.Health = Math.Max(0, bearerVitals.Health - damage);
            total += damage;

            if (bearerVitals.Health <= 0)
            {
                break;
            }
        }

        return total;
    }

    // ---------------------------------------------------------------------------------------
    // The rotation
    // ---------------------------------------------------------------------------------------

    /// <summary>What the rotation decided, and whether the resource bar was the reason.</summary>
    private readonly record struct Choice(Ability? Ability, bool Starved);

    /// <summary>
    /// Which ability to press, if any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Heal first, then a missing damage buff, then the biggest thing that is not redundant.</b>
    /// This is a competent player rather than an optimal one, and the difference matters for what
    /// the result means: an optimal rotation would be measuring how well the harness plays, and a
    /// naive one - press the first thing off cooldown - would flatter every Path with a cheap
    /// level 1 button by pressing it forever.
    /// </para>
    /// <para>
    /// <b>Ranked by expected damage per cast, not per second.</b> Abilities do not block each other
    /// - there is no global cooldown in this game and a cast does not delay a swing - so anything
    /// off cooldown and affordable is free to press. What the ranking decides is only the order
    /// within one pulse, where the resource bar is the constraint, and a bar is spent per cast.
    /// </para>
    /// </remarks>
    private Choice Choose(
        List<Ability> known,
        Character character,
        Dictionary<string, long> lastCast,
        long pulse,
        EffectBag targetEffects,
        EffectBag selfEffects,
        string selfSource)
    {
        if (known.Count == 0)
        {
            return new Choice(null, false);
        }

        var ready = new List<Ability>();
        var starved = false;

        foreach (var ability in known)
        {
            var blocked = AbilityCooldowns.Blocking(
                ability,
                known,
                a => lastCast.TryGetValue(a.Key, out var p) ? p : null,
                pulse);

            if (blocked is not null)
            {
                continue;
            }

            if (!CanAfford(ability, character))
            {
                starved = true;
                continue;
            }

            ready.Add(ability);
        }

        // Starving means nothing off cooldown could be paid for - not that *something* could not.
        //
        // The looser reading is what this counted first, and it is close to useless: a level 50
        // Path knows eighteen abilities, several are always off cooldown, and the most expensive of
        // them is unaffordable on a nearly full bar - so it reported 40-70% of every high-level
        // fight as starved regardless of what was actually happening. It survived because it moves
        // in roughly the right direction; it just has no zero.
        if (ready.Count > 0)
        {
            starved = false;
        }

        if (ready.Count == 0)
        {
            return new Choice(null, starved);
        }

        // Out of the resource the kit runs on, with something in the bar that refills it. Ranked
        // ahead of the heal because it is upstream of everything: a caster with no focus cannot
        // heal either, and the ability that fixes that is on a five-minute timer.
        var restore = ready
            .Where(a => RestoreTarget(a) is { } bar && ShareOf(character, bar) < RestoreBelow)
            .OrderByDescending(a => RestoreValue(a, character))
            .FirstOrDefault();

        if (restore is not null)
        {
            return new Choice(restore, starved);
        }

        // Hurt enough to want the heal that exists.
        if ((double)character.Vitals.Health / Math.Max(1, character.Vitals.HealthMax) < HealBelow)
        {
            var heal = ready
                .Where(a => a.Effects.Any(e => string.Equals(e.Key, "heal.restore", StringComparison.Ordinal)))
                .OrderByDescending(a => HealValue(a, character))
                .FirstOrDefault();

            if (heal is not null)
            {
                return new Choice(heal, starved);
            }
        }

        // A damage buff that is not currently up is worth more than any single cast, because it
        // multiplies every swing and every wound tick for its whole duration.
        var buff = ready
            .Where(a => RaisesDamage(a) && !Redundant(a, pulse, targetEffects, selfEffects, selfSource))
            .OrderByDescending(BuffValue)
            .FirstOrDefault();

        if (buff is not null)
        {
            return new Choice(buff, starved);
        }

        var best = ready
            .Where(a => !Redundant(a, pulse, targetEffects, selfEffects, selfSource))
            .Select(a => (Ability: a, Value: ExpectedDamage(a, character.Level)))
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .Select(x => x.Ability)
            .FirstOrDefault();

        return new Choice(best, starved);
    }

    private static bool CanAfford(Ability ability, Character character) => ability.CostType switch
    {
        CostType.Focus => character.Vitals.Focus >= ability.CostValue,
        CostType.Stamina => character.Vitals.Stamina >= ability.CostValue,

        // Health is spendable but not to death: a rotation that kills its own caster is not a
        // measurement of anything.
        CostType.Health => character.Vitals.Health > ability.CostValue,
        _ => false,
    };

    /// <summary>
    /// Whether every ongoing thing this ability does is already running with time to spare.
    /// </summary>
    private bool Redundant(
        Ability ability, long pulse, EffectBag targetEffects, EffectBag selfEffects, string selfSource)
    {
        var ongoing = 0;
        var covered = 0;

        foreach (var spec in ability.Effects)
        {
            if (_effects.Get(spec.Key) is not IBuffEffect executor)
            {
                continue;
            }

            ongoing++;

            var bag = executor.IsHarmful ? targetEffects : selfEffects;
            var duration = DurationOf(spec.Params);
            var remaining = bag.Remaining(spec.Key, selfSource, pulse);

            if (duration > 0 && remaining > duration * RefreshBelow)
            {
                covered++;
            }
        }

        // An ability with an instant component is never redundant - Rupture's bleed may be up, but
        // its opening strike is damage that has not been dealt yet.
        return ongoing > 0 && ongoing == covered && !HasInstantDamage(ability);
    }

    /// <summary>
    /// How long an effect built from these parameters would run.
    /// </summary>
    /// <remarks>
    /// Read from the bag rather than by building a probe effect: every <c>IBuffEffect</c> in the
    /// game reads <c>durationPulses</c>, and building one means handing an executor a caster to
    /// stamp a source id from, which there is not one of at the point the rotation is only asking
    /// a question.
    /// </remarks>
    private static long DurationOf(Dictionary<string, string> parameters) =>
        Read(parameters, "durationPulses", 0);

    private bool HasInstantDamage(Ability ability) =>
        ability.Effects.Any(e => _effects.Get(e.Key) is { } x and not IBuffEffect && x.IsHarmful);

    /// <summary>Which bar this ability refills, or null when it refills none.</summary>
    private static CostType? RestoreTarget(Ability ability)
    {
        foreach (var spec in ability.Effects)
        {
            if (string.Equals(spec.Key, "resource.restore", StringComparison.Ordinal))
            {
                return ResourceEffect.ResourceOf(spec.Params);
            }
        }

        return null;
    }

    /// <summary>How full one of a character's bars is, 0-1.</summary>
    private static double ShareOf(Character character, CostType bar) => bar switch
    {
        CostType.Focus => character.Vitals.FocusMax == 0
            ? 1
            : (double)character.Vitals.Focus / character.Vitals.FocusMax,
        CostType.Stamina => character.Vitals.StaminaMax == 0
            ? 1
            : (double)character.Vitals.Stamina / character.Vitals.StaminaMax,
        _ => character.Vitals.HealthMax == 0
            ? 1
            : (double)character.Vitals.Health / character.Vitals.HealthMax,
    };

    /// <summary>How much a restore would actually put back, for this character.</summary>
    private static double RestoreValue(Ability ability, Character character) =>
        ability.Effects
            .Where(e => string.Equals(e.Key, "resource.restore", StringComparison.Ordinal))
            .Select(e => (double)ResourceEffect.Amount(e.Params, MaximumOf(character, ResourceEffect.ResourceOf(e.Params))))
            .DefaultIfEmpty(0)
            .Max();

    private static int MaximumOf(Character character, CostType bar) => bar switch
    {
        CostType.Focus => character.Vitals.FocusMax,
        CostType.Stamina => character.Vitals.StaminaMax,
        _ => character.Vitals.HealthMax,
    };

    private static bool RaisesDamage(Ability ability) =>
        ability.Effects.Any(e =>
            e.Params.TryGetValue("outgoingMultiplier", out var raw) &&
            decimal.TryParse(raw, out var value) && value > 1m);

    private static double BuffValue(Ability ability) =>
        ability.Effects
            .Select(e => e.Params.TryGetValue("outgoingMultiplier", out var raw) &&
                         double.TryParse(raw, out var value)
                ? value
                : 1.0)
            .Max();

    /// <summary>
    /// What the biggest heal on this ability is worth to <em>this</em> character.
    /// </summary>
    /// <remarks>
    /// The maximum is passed because a proportional heal has no answer without one - asking
    /// <c>Middle</c> with no target falls back to the flat default, which would rank a heal worth
    /// half a health bar below one worth twenty points.
    /// </remarks>
    private static double HealValue(Ability ability, Character character) =>
        ability.Effects
            .Where(e => string.Equals(e.Key, "heal.restore", StringComparison.Ordinal))
            .Select(e => (double)HealEffect.Middle(e.Params, character.Vitals.HealthMax))
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// What one cast of this is worth, in damage, counting a wound's whole run.
    /// </summary>
    /// <remarks>
    /// Read through the executors' own statics rather than from the parameter bag directly, so that
    /// the day <c>DamageEffect</c> stops multiplying a constant this follows without being touched -
    /// which is the change this harness exists to measure.
    /// </remarks>
    private double ExpectedDamage(Ability ability, int casterLevel)
    {
        var total = 0.0;

        foreach (var spec in ability.Effects)
        {
            switch (spec.Key)
            {
                case "damage.physical":
                    total += DamageEffect.Middle(spec.Params, casterLevel);
                    break;

                case "damage.overtime":
                    var tick = Read(spec.Params, "tickDamage", 4);
                    var interval = Read(spec.Params, "tickIntervalPulses", 8);
                    var duration = Read(spec.Params, "durationPulses", 48);
                    total += tick * DamageOverTimeEffect.TickCount(duration, interval);
                    break;
            }
        }

        return total;
    }

    private static long Read(Dictionary<string, string> parameters, string key, long fallback) =>
        parameters.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : fallback;

    // ---------------------------------------------------------------------------------------
    // Casting
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Spends the cost, runs every executor in order, and leaves behind whatever ongoing state
    /// they create. The order and the one-cost-per-ability rule are <c>AbilitySystem</c>'s.
    /// </summary>
    private void Cast(
        Ability ability,
        Character caster,
        Mob target,
        IRandomSource random,
        long pulse,
        EffectBag casterEffects,
        EffectBag targetEffects,
        string casterSource,
        Dictionary<string, long> lastCast)
    {
        switch (ability.CostType)
        {
            case CostType.Focus:
                caster.Vitals.Focus = Math.Max(0, caster.Vitals.Focus - ability.CostValue);
                break;
            case CostType.Stamina:
                caster.Vitals.Stamina = Math.Max(0, caster.Vitals.Stamina - ability.CostValue);
                break;
            case CostType.Health:
                caster.Vitals.Health = Math.Max(0, caster.Vitals.Health - ability.CostValue);
                break;
        }

        lastCast[ability.Key] = pulse;

        foreach (var spec in ability.Effects)
        {
            if (_effects.Get(spec.Key) is not { } executor)
            {
                continue;
            }

            // Where it points decides who it lands on, asked of the executor rather than guessed
            // from the ability - the rule AbilitySystem uses, and the reason casting Scorch with no
            // target once set the caster on fire.
            object landsOn = executor.IsHarmful ? target : caster;

            executor.Apply(caster, landsOn, spec.Params, random);

            if (executor is not IBuffEffect buff)
            {
                continue;
            }

            var effect = buff.CreateActiveEffect(caster, landsOn, spec.Params, pulse);
            effect.SourceUnlockLevel = ability.UnlockLevel;

            if (executor.IsHarmful)
            {
                targetEffects.Apply(effect, target.Vitals);
            }
            else
            {
                casterEffects.Apply(effect, caster.Vitals);
            }
        }
    }
}
