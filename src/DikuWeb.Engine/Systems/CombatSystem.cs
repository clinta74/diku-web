using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;
using Microsoft.Extensions.Logging;
using DomainCombatSystem = DikuWeb.Domain.Combat.CombatSystem;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Combat runs every pulse, and every combatant swings on its own clock: a player's from the
/// weapons they wield, a mob's from each entry in its template's attack list.
/// </summary>
/// <remarks>
/// It ticks every pulse rather than every eighth because that is the finest grain the loop has,
/// and a shared eight-pulse round cannot express "this dagger is faster than that maul". The work
/// per pulse is bounded by readiness checks, not by attacks: most pulses nothing is due.
///
/// It is synchronous on purpose. The previous version was async and launched fire-and-forget from
/// the loop, so mob death - awarding XP, spawning loot, removing the mob - ran on a thread-pool
/// thread while the loop kept mutating the same dictionaries. <see cref="WorldState"/> is
/// single-writer by contract, so that was a race, not a style choice. Template lookups now come
/// from the caches the applier keeps live, which removes the last reason to await.
/// </remarks>
public sealed class CombatSystem(
    EngineOptions options,
    PlayerView? view = null,
    ItemTemplateCache? itemTemplates = null,
    MobTemplateCache? mobTemplates = null,
    ItemSpawner? itemSpawner = null,
    ILogger<CombatSystem>? logger = null,
    EffectRegistry? effects = null)
{
    /// <summary>Combat is evaluated every pulse; per-attack delays do the pacing.</summary>
    public const int TickIntervalPulses = 1;

    /// <summary>
    /// True when the template caches were supplied. Without them every weapon swings at the
    /// default speed and every mob narrates "hit" - a balance-shaped bug rather than a crash,
    /// which is why the DI wiring is asserted in tests rather than trusted.
    /// </summary>
    internal bool HasTemplateCaches => itemTemplates is not null && mobTemplates is not null;

    /// <summary>
    /// Resolve every fight for one pulse: swings that have come due, deaths they caused, and
    /// fights that are over.
    /// </summary>
    public void Tick(WorldState world, long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(world);

        List<Combat>? active = null;
        List<RoomKey>? finished = null;

        foreach (var combat in world.AllCombats)
        {
            if (combat.Combatants.Count == 0)
            {
                (finished ??= []).Add(combat.RoomKey);
            }
            else
            {
                (active ??= []).Add(combat);
            }
        }

        if (active is not null)
        {
            // One pass over world items for the whole tick. Readiness is checked per attack per
            // pulse, and resolving equipment inside that check would mean scanning every item in
            // the world several times a second per fighter.
            var equipment = EquipmentInCombat(world, active);

            foreach (var combat in active)
            {
                // Wounds tick before swings. A bleed that finishes something should resolve
                // before it gets another turn, the same way the blow that kills ends the
                // exchange rather than being swept up afterwards.
                TickDamageOverTime(world, combat, currentPulse);

                foreach (var combatantId in combat.Combatants.ToList())
                {
                    // A death or a departure earlier in this same pulse may already have taken
                    // this combatant out of the fight.
                    if (!combat.Combatants.Contains(combatantId))
                    {
                        continue;
                    }

                    combat.MarkEngaged(combatantId, currentPulse);
                    RunCombatant(world, combat, combatantId, currentPulse, equipment);
                }

                if (!IsCombatActive(combat))
                {
                    foreach (var combatantId in combat.Combatants)
                    {
                        EndCombatFor(world, combatantId);
                    }

                    combat.Combatants.Clear();
                    (finished ??= []).Add(combat.RoomKey);
                }

                combat.RoundNumber++;
            }
        }

        // Reclaim the empties. Nothing used to, so a room that had ever seen a fight was walked
        // on every tick for the life of the process.
        if (finished is not null)
        {
            foreach (var roomKey in finished)
            {
                world.EndCombat(roomKey);
            }
        }
    }

    /// <summary>
    /// Everything equipped by the characters currently in a fight, keyed by owner.
    /// </summary>
    private static Dictionary<Guid, List<ItemInstance>> EquipmentInCombat(
        WorldState world,
        List<Combat> active)
    {
        var owners = new HashSet<Guid>();

        foreach (var combat in active)
        {
            foreach (var combatantId in combat.Combatants)
            {
                if (EntityId.IsCharacter(combatantId))
                {
                    owners.Add(EntityId.ToGuid(combatantId));
                }
            }
        }

        var equipment = new Dictionary<Guid, List<ItemInstance>>();

        if (owners.Count == 0)
        {
            return equipment;
        }

        foreach (var item in world.AllItems)
        {
            if (item.EquippedSlot is null ||
                item.OwnerCharacterId is not { } owner ||
                !owners.Contains(owner))
            {
                continue;
            }

            if (!equipment.TryGetValue(owner, out var held))
            {
                held = [];
                equipment[owner] = held;
            }

            held.Add(item);
        }

        return equipment;
    }

    /// <summary>Runs whichever of one combatant's attacks have come due this pulse.</summary>
    private void RunCombatant(
        WorldState world,
        Combat combat,
        string attackerId,
        long currentPulse,
        Dictionary<Guid, List<ItemInstance>> equipment)
    {
        // The dead do not swing. Deaths are resolved the instant the blow lands, so by the time
        // a killed combatant would have acted it is already out of the fight - this is the
        // second line of defence, not the first.
        if (!IsAlive(world, attackerId))
        {
            return;
        }

        // Neither do the stunned. This is the gate the whole effect hangs on: swings are the
        // only thing a mob does in combat, so missing it here would make a stun cosmetic.
        if (world.IsStunned(EntityId.ToGuid(attackerId), currentPulse))
        {
            return;
        }

        // A character committed to a spell is not also swinging a sword.
        if (EntityId.IsCharacter(attackerId) &&
            world.CastQueue.IsCasting(EntityId.ToGuid(attackerId)))
        {
            return;
        }

        var targetId = ResolveTargetOf(combat, attackerId);
        if (string.IsNullOrEmpty(targetId) || targetId == attackerId)
        {
            return;
        }

        // Validate both combatants are in the same room
        var attackerRoom = GetCombatantRoom(world, attackerId);
        var targetRoom = GetCombatantRoom(world, targetId);
        if (attackerRoom != combat.RoomKey || targetRoom != combat.RoomKey)
        {
            // Combatant left the room, remove them from combat
            combat.RemoveCombatant(attackerId);
            return;
        }

        var (attackerType, attackerName, attackerActor) = ResolveCombatantInfo(world, attackerId);
        var (targetType, targetName, targetActor) = ResolveCombatantInfo(world, targetId);
        if (attackerType is null || targetType is null)
        {
            return;
        }

        // Whether the fight is allowed at all is re-checked every tick, so clearing `pvp` on a
        // room ends a duel already under way (PLAN.md §4.11).
        var peaceful = world.IsFlagSet(combat.RoomKey, RoomFlags.Peaceful);
        var pvp = world.IsFlagSet(combat.RoomKey, RoomFlags.Pvp);
        var validation = TargetValidator.ValidateTarget(
            attackerType.Value, targetType.Value, targetName, peaceful, pvp);

        if (!validation.IsAllowed)
        {
            if (attackerActor is PlayerActor refusedPlayer)
            {
                refusedPlayer.SendText(validation.RefusalReason ?? "The attack is not allowed.", "bad");
            }

            combat.RemoveCombatant(attackerId);

            if (EntityId.IsCharacter(attackerId) &&
                world.GetCharacter(EntityId.ToGuid(attackerId)) is { } refused)
            {
                refused.CombatState = CombatState.Idle;
                refused.CurrentTarget = null;
            }

            return;
        }

        var strike = new StrikeContext(
            combat,
            attackerId,
            targetId,
            attackerType.Value,
            attackerName,
            attackerActor,
            targetType.Value,
            targetName,
            targetActor,
            validation,
            currentPulse);

        if (EntityId.IsCharacter(attackerId))
        {
            RunHandAttacks(world, strike, equipment);
        }
        else
        {
            RunMobAttacks(world, strike);
        }
    }

    /// <summary>
    /// Swings the main hand, then the off hand. Order matters: without ambidexterity the off
    /// hand is gated on the main hand having swung, and it reads the stamp the main hand just
    /// wrote, which is what makes the pair land together.
    /// </summary>
    private void RunHandAttacks(
        WorldState world,
        StrikeContext strike,
        Dictionary<Guid, List<ItemInstance>> equipment)
    {
        var character = world.GetCharacter(EntityId.ToGuid(strike.AttackerId));
        if (character is null)
        {
            return;
        }

        var held = equipment.TryGetValue(character.Id, out var equipped)
            ? equipped
            : (IReadOnlyList<ItemInstance>)[];

        var combat = strike.Combat;
        var pulse = strike.Pulse;

        var main = WeaponResolver.ForHand(
            InSlot(held, ItemSlot.MainHand), LookupWeapon, isMainHand: true);

        if (main.CanSwing && IsDue(combat, strike.AttackerId, AttackSlot.MainHand, pulse, main.DelayPulses))
        {
            var stats = EquipmentResolver.ResolveAttackerStatsForHand(
                character.Level, character.Attributes.MightModifier, held, ItemSlot.MainHand);

            if (Strike(world, strike, AttackSlot.MainHand, stats, main.Verb))
            {
                return;
            }
        }

        var offHandItem = InSlot(held, ItemSlot.OffHand);
        var off = WeaponResolver.ForHand(offHandItem, LookupWeapon, isMainHand: false);

        if (!off.CanSwing || !CanStrikeWithOffHand(character))
        {
            return;
        }

        var lastOff = combat.LastSwing(strike.AttackerId, AttackSlot.OffHand);
        var basis = lastOff ?? combat.EngagedAt(strike.AttackerId);

        if (pulse - basis < off.DelayPulses)
        {
            return;
        }

        // Ambidexterity is what frees the off hand from the main hand's rhythm. Untrained, it
        // may only follow a main-hand blow, so the two land together at the main hand's cadence.
        if (!AbilityProgression.KnowsPassive(character.Path, character.Level, PassiveKeys.Ambidextrous))
        {
            var lastMain = combat.LastSwing(strike.AttackerId, AttackSlot.MainHand);
            if (lastMain is not { } mainSwing || (lastOff is { } offSwing && mainSwing <= offSwing))
            {
                return;
            }
        }

        var offStats = EquipmentResolver.ResolveAttackerStatsForHand(
            character.Level, character.Attributes.MightModifier, held, ItemSlot.OffHand);

        Strike(world, strike, AttackSlot.OffHand, offStats, off.Verb);
    }

    /// <summary>Every entry in the mob's attack list runs its own clock, coupled to nothing.</summary>
    private void RunMobAttacks(WorldState world, StrikeContext strike)
    {
        var mob = world.GetMob(EntityId.ToGuid(strike.AttackerId));
        if (mob is null)
        {
            return;
        }

        var attacks = MobAttackResolver.Resolve(mobTemplates?.Get(mob.TemplateKey));
        var baseStats = DamageCalculator.StatsFrom(mob);

        for (var index = 0; index < attacks.Count; index++)
        {
            var attack = attacks[index];
            var slot = AttackSlot.Mob(index);

            if (!IsDue(strike.Combat, strike.AttackerId, slot, strike.Pulse, attack.DelayPulses))
            {
                continue;
            }

            if (Strike(world, strike, slot, Scale(baseStats, attack.DamageMultiplier), attack.Verb, attack))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Applies the effect an attack carries, if it carries one and the blow landed.
    /// </summary>
    /// <remarks>
    /// The whole of "mobs can stun, snare, and bleed" (PLAN.md §12). It is this small because the
    /// receiving side was already built for players: <c>ActiveEffect</c>, <c>ApplyEffect</c>,
    /// <c>IsStunned</c> gating the cast command and the combat loop, <c>PreventsEscape</c> gating
    /// <c>flee</c>. Only the emitting side was missing, and an attack is a better home for it than
    /// a spellbook - the swing already has a timer, already rolls to hit, and already resolves
    /// damage, so the effect inherits the miss chance and the parry for free. A stun you dodged
    /// should not stun you.
    ///
    /// Unknown keys are ignored rather than refused. The registry is the authority on what exists,
    /// and a mob template naming an effect this build does not have should swing for its damage
    /// rather than stop swinging - the same "absence is the safe value" rule the flags follow.
    /// </remarks>
    private void ApplyRider(WorldState world, StrikeContext strike, MobAttack? rider)
    {
        if (rider?.EffectKey is not { } key ||
            effects?.Get(key) is not { } effect ||
            !EntityId.IsWellFormed(strike.AttackerId) ||
            !EntityId.IsWellFormed(strike.TargetId))
        {
            return;
        }

        object? source = EntityId.IsMob(strike.AttackerId)
            ? world.GetMob(EntityId.ToGuid(strike.AttackerId))
            : world.GetCharacter(EntityId.ToGuid(strike.AttackerId));

        object? target = EntityId.IsMob(strike.TargetId)
            ? world.GetMob(EntityId.ToGuid(strike.TargetId))
            : world.GetCharacter(EntityId.ToGuid(strike.TargetId));

        if (source is null || target is null)
        {
            return;
        }

        var parameters = rider.EffectParams ?? [];

        effect.Apply(source, target, parameters, world.Random);

        if (effect is IBuffEffect ongoing)
        {
            world.ApplyEffect(
                EntityId.ToGuid(strike.TargetId),
                ongoing.CreateActiveEffect(source, target, parameters, strike.Pulse));
        }

        NarrateRider(world, strike, effect.EffectKey, parameters);
    }

    /// <summary>
    /// Says that something other than damage just happened.
    /// </summary>
    /// <remarks>
    /// Without this a stun is invisible: the player's next command is refused with "You cannot
    /// gather yourself" and nothing ever explained why. The effect's own <c>name</c> parameter is
    /// the wording, because that is what the status panel and the ability that applied it already
    /// use - "reeling" reads the same wherever it came from.
    /// </remarks>
    private static void NarrateRider(
        WorldState world,
        StrikeContext strike,
        string effectKey,
        Dictionary<string, string> parameters)
    {
        var label = parameters.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : effectKey;

        if (strike.TargetActor is PlayerActor targetPlayer)
        {
            targetPlayer.SendText($"You are {label}!", "bad");
        }

        foreach (var occupant in world.OccupantsOf(strike.Combat.RoomKey))
        {
            if (occupant.CharacterId != (strike.TargetActor as PlayerActor)?.CharacterId)
            {
                occupant.SendText($"{strike.TargetName} is {label}!", "combat");
            }
        }
    }

    /// <summary>
    /// Resolves one swing: narration, damage, hate, and the target's death if it caused one.
    /// </summary>
    /// <returns>True when the fight is over for this attacker - the target died or left.</returns>
    /// <param name="rider">
    /// An effect this attack carries, applied on a landed hit. Mobs only: a player's effects come
    /// from abilities, which have a cost and a cooldown to pay for them.
    /// </param>
    private bool Strike(
        WorldState world,
        StrikeContext strike,
        AttackSlot slot,
        AttackerStats attackerStats,
        string verb,
        MobAttack? rider = null)
    {
        if (!IsAlive(world, strike.TargetId))
        {
            return true;
        }

        var defenderStats = ResolveDefenderStats(world, strike.TargetId);
        if (defenderStats is null)
        {
            return true;
        }

        strike.Combat.RecordSwing(strike.AttackerId, slot, strike.Pulse);

        var targetHealth = HealthOf(world, strike.TargetId) ?? 0;

        var round = DomainCombatSystem.ExecuteRound(
            strike.AttackerType,
            strike.AttackerName,
            attackerStats,
            strike.TargetType,
            strike.TargetName,
            defenderStats,
            targetHealth,
            strike.Validation,
            verb,
            world.Random);

        // Parry is checked after the roll and before the narration, so it only ever spends
        // itself on a blow that was going to land - and the exchange is narrated once, as a
        // parry, rather than as a hit that is then quietly undone.
        if (round.Damage.Hit && TryParry(world, strike, out var parryNarration))
        {
            NarrateToRoom(world, strike, parryNarration);
            return false;
        }

        if (strike.AttackerActor is PlayerActor attackerPlayer)
        {
            attackerPlayer.SendText(round.AttackerNarration, "combat");
        }

        if (strike.TargetActor is PlayerActor targetPlayer)
        {
            targetPlayer.SendText(round.TargetNarration, "combat");
        }

        foreach (var occupant in world.OccupantsOf(strike.Combat.RoomKey))
        {
            if (occupant.CharacterId != (strike.AttackerActor as PlayerActor)?.CharacterId &&
                occupant.CharacterId != (strike.TargetActor as PlayerActor)?.CharacterId)
            {
                occupant.SendText(round.RoomNarration, "combat");
            }
        }

        if (!round.Damage.Hit)
        {
            return false;
        }

        // Buff and debuff multipliers apply after the roll, so a shout of fury and a curse of
        // weakness compose rather than one overwriting the other.
        var attackerEffects = world.GetActiveEffects(EntityId.ToGuid(strike.AttackerId));
        var targetEffects = world.GetActiveEffects(EntityId.ToGuid(strike.TargetId));
        var totalMultiplier =
            CalculateMultiplier(attackerEffects, e => e.OutgoingDamageMultiplier) *
            CalculateMultiplier(targetEffects, e => e.IncomingDamageMultiplier);

        var damageDealt = Math.Max(
            0,
            (int)Math.Round(round.Damage.DamageDealt * totalMultiplier, MidpointRounding.AwayFromZero));

        ApplyDamage(world, strike.TargetId, damageDealt);

        if (EntityId.IsMob(strike.TargetId))
        {
            strike.Combat.AddToHateList(strike.TargetId, strike.AttackerId, round.Damage.DamageDealt);
        }

        if (HealthOf(world, strike.TargetId) > 0)
        {
            // Only on a survivor. Stunning something the same blow just killed is wasted work,
            // and a bleed on a corpse would tick against a dead thing.
            ApplyRider(world, strike, rider);
            return false;
        }

        // The blow that kills ends the exchange here and now. Sweeping for deaths after every
        // combatant had acted is what let a mob killed by the player's swing hit back before
        // falling.
        HandleDeath(world, strike.Combat, strike.TargetId);
        return true;
    }

    /// <summary>
    /// Rolls the defender's parry against a blow that would otherwise have landed.
    /// </summary>
    /// <remarks>
    /// Only characters parry: the chance comes from Path and level, and a mob has neither. That
    /// is deliberate rather than an oversight - parrying is a trained skill the two martial Paths
    /// learn, and giving it to mobs would make every fight longer without making any of them more
    /// interesting.
    /// </remarks>
    private static bool TryParry(WorldState world, StrikeContext strike, out string narration)
    {
        narration = string.Empty;

        if (!EntityId.IsCharacter(strike.TargetId))
        {
            return false;
        }

        var defender = world.GetCharacter(EntityId.ToGuid(strike.TargetId));
        if (defender is null)
        {
            return false;
        }

        var chance = AbilityProgression.ParryChance(defender.Path, defender.Level);
        if (chance <= 0 || !world.Random.Chance(chance))
        {
            return false;
        }

        narration = $"{strike.TargetName} parries {strike.AttackerName}'s attack.";
        return true;
    }

    /// <summary>Sends one line to both combatants and everyone watching.</summary>
    private static void NarrateToRoom(WorldState world, StrikeContext strike, string line)
    {
        foreach (var occupant in world.OccupantsOf(strike.Combat.RoomKey))
        {
            occupant.SendText(line, "combat");
        }
    }

    /// <summary>
    /// Applies every bleed, burn, and poison that is due on this pulse.
    /// </summary>
    /// <remarks>
    /// Inside the combat loop rather than in its own system, because that is where the pieces
    /// already are: <see cref="ApplyDamage"/>, <see cref="HandleDeath"/>, and with them the XP,
    /// the loot, and the corpse. A separate ticker would have to reach for a <see cref="Combat"/>
    /// to kill anything, and a bleed that could not land the killing blow would be a strange kind
    /// of wound.
    ///
    /// The consequence is that wounds only tick during a fight. Fleeing stops the bleeding, which
    /// is a real balance decision rather than an accident - and it falls out of §4.11's rule that
    /// leaving ends the fight immediately.
    /// </remarks>
    private void TickDamageOverTime(WorldState world, Combat combat, long currentPulse)
    {
        foreach (var combatantId in combat.Combatants.ToList())
        {
            if (!combat.Combatants.Contains(combatantId) || !IsAlive(world, combatantId))
            {
                continue;
            }

            foreach (var effect in world.GetActiveEffects(EntityId.ToGuid(combatantId)))
            {
                // Expiry is checked here as well as by the sweep that removes it. The sweep runs
                // on the 60s tick and this runs every pulse, so relying on it alone would let a
                // wound keep working for up to a minute after it ran out.
                if (!effect.Ticks ||
                    effect.NextTickPulse > currentPulse ||
                    effect.ExpiresAtPulse <= currentPulse)
                {
                    continue;
                }

                effect.NextTickPulse = currentPulse + effect.TickIntervalPulses;

                // Stacks multiply the wound rather than each keeping its own clock, so five
                // stacks of a bleed read as one worse bleed instead of five overlapping ones.
                var damage = effect.TickDamage * Math.Max(1, effect.Stacks);

                ApplyDamage(world, combatantId, damage);

                // Credited to whoever applied it, not to whoever is standing nearby. A Shade's
                // Ambush is most of that Path's damage, and without this none of it was worth any
                // threat - the bleed did the work and the melee got the blame.
                ThreatCredit.CreditTick(
                    world, combat.RoomKey, effect.SourceEntityId, combatantId, damage);

                NarrateTick(world, combat, combatantId, effect, damage);

                if (HealthOf(world, combatantId) <= 0)
                {
                    HandleDeath(world, combat, combatantId);
                    break;
                }
            }
        }
    }

    /// <summary>Tells the room that a wound is still working.</summary>
    private static void NarrateTick(
        WorldState world,
        Combat combat,
        string combatantId,
        ActiveEffect effect,
        int damage)
    {
        var isCharacter = EntityId.IsCharacter(combatantId);
        var name = isCharacter
            ? world.GetCharacter(EntityId.ToGuid(combatantId))?.Name
            : world.GetMob(EntityId.ToGuid(combatantId)) is { } mob
                ? (string.IsNullOrEmpty(mob.TemplateName) ? mob.TemplateKey : mob.TemplateName)
                : null;

        if (name is null)
        {
            return;
        }

        foreach (var occupant in world.OccupantsOf(combat.RoomKey))
        {
            var isVictim = isCharacter &&
                           occupant.CharacterId == EntityId.ToGuid(combatantId);

            occupant.SendText(
                isVictim
                    ? $"Your {effect.Name} costs you {damage}."
                    : $"{NarrationHelper.WithDefiniteArticle(name, capitalize: true)} suffers {damage} from {effect.Name}.",
                "combat");
        }
    }

    /// <summary>Whether this attack's delay has elapsed since it last swung, or since engaging.</summary>
    private static bool IsDue(Combat combat, string attackerId, AttackSlot slot, long pulse, int delayPulses) =>
        pulse - (combat.LastSwing(attackerId, slot) ?? combat.EngagedAt(attackerId)) >= delayPulses;

    /// <summary>
    /// Whether this character has been taught to fight with a second weapon. Untrained, an
    /// off-hand weapon is carried, not swung - so an Adept may hold a blade there and it simply
    /// never strikes.
    /// </summary>
    private static bool CanStrikeWithOffHand(Character character) =>
        AbilityProgression.KnowsPassive(character.Path, character.Level, PassiveKeys.DualWield);

    private static ItemInstance? InSlot(IReadOnlyList<ItemInstance> held, ItemSlot slot)
    {
        foreach (var item in held)
        {
            if (item.EquippedSlot == slot)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Speed and verb come from the template, read fresh on every readiness check, so a builder
    /// retuning a weapon changes a fight already under way - and so does swapping weapons.
    /// </summary>
    private (int? DelayPulses, string? Verb) LookupWeapon(string templateKey)
    {
        var template = itemTemplates?.Get(templateKey);
        return (template?.AttackDelayPulses, template?.AttackVerb);
    }

    /// <summary>
    /// Scales an attack's dice against the mob's own damage. A multiplier rather than its own
    /// dice keeps zone and world multipliers in play, so a hard bite stays hard relative to
    /// wherever the mob spawned.
    /// </summary>
    private static AttackerStats Scale(AttackerStats stats, decimal? multiplier)
    {
        if (multiplier is not { } scale || scale <= 0m || scale == 1m)
        {
            return stats;
        }

        // Ceiling, so a multiplier can never round a die face down to nothing.
        return stats with
        {
            MinDamage = (int)Math.Ceiling(stats.MinDamage * scale),
            MaxDamage = (int)Math.Ceiling(Math.Max(stats.MinDamage, stats.MaxDamage) * scale),
        };
    }

    private static string? ResolveTargetOf(Combat combat, string attackerId)
    {
        if (EntityId.IsCharacter(attackerId))
        {
            // Player: use their chosen target
            return combat.PlayerTargets.TryGetValue(EntityId.ToGuid(attackerId), out var target)
                ? target
                : null;
        }

        // Mob: use top hater
        return EntityId.IsMob(attackerId) ? combat.GetTopHater(attackerId) : null;
    }

    private static bool IsAlive(WorldState world, string entityId) =>
        HealthOf(world, entityId) is > 0;

    private static int? HealthOf(WorldState world, string entityId)
    {
        if (EntityId.IsCharacter(entityId))
        {
            return world.GetCharacter(EntityId.ToGuid(entityId))?.Vitals.Health;
        }

        return EntityId.IsMob(entityId)
            ? world.GetMob(EntityId.ToGuid(entityId))?.Vitals.Health
            : null;
    }

    private static RoomKey? GetCombatantRoom(WorldState world, string entityId)
    {
        if (EntityId.IsCharacter(entityId))
        {
            return world.GetCharacter(EntityId.ToGuid(entityId))?.RoomKey;
        }

        if (EntityId.IsMob(entityId))
        {
            var mob = world.GetMob(EntityId.ToGuid(entityId));
            return mob != null ? RoomKey.Parse(mob.RoomKey) : null;
        }

        return null;
    }

    /// <summary>
    /// Resolves a combatant's type, name, and actor reference for display and narration.
    /// </summary>
    private static (CombatantType?, string, object?) ResolveCombatantInfo(WorldState world, string entityId)
    {
        if (EntityId.IsCharacter(entityId))
        {
            var actor = world.FindByCharacter(EntityId.ToGuid(entityId));
            if (actor != null)
            {
                return (CombatantType.Player, actor.Name, actor);
            }
        }
        else if (EntityId.IsMob(entityId))
        {
            var mob = world.FindMob(EntityId.ToGuid(entityId));
            if (mob != null)
            {
                return (CombatantType.Mob, DisplayNameOf(mob), mob);
            }
        }

        return (null, "", null);
    }

    private static string DisplayNameOf(Mob mob) =>
        string.IsNullOrEmpty(mob.TemplateName) ? mob.TemplateKey : mob.TemplateName;

    private static DefenderStats? ResolveDefenderStats(WorldState world, string targetId)
    {
        if (EntityId.IsCharacter(targetId))
        {
            var character = world.GetCharacter(EntityId.ToGuid(targetId));
            return character is null
                ? null
                : EquipmentResolver.ResolveDefenderStats(
                    character.Attributes.AgilityModifier,
                    [.. world.InventoryOf(character.Id).Where(i => i.EquippedSlot is not null)]);
        }

        if (EntityId.IsMob(targetId))
        {
            var mob = world.GetMob(EntityId.ToGuid(targetId));
            return mob is null ? null : DamageCalculator.DefenderStatsFrom(mob);
        }

        return null;
    }

    private static decimal CalculateMultiplier(
        IReadOnlyList<DikuWeb.Domain.Abilities.Effects.ActiveEffect> effects,
        Func<DikuWeb.Domain.Abilities.Effects.ActiveEffect, decimal> selector)
    {
        var multiplier = 1.0m;

        foreach (var effect in effects)
        {
            var effectMultiplier = selector(effect);
            if (effectMultiplier != 1.0m)
            {
                var stackMultiplier = effect.StackingRule == DikuWeb.Domain.Abilities.Effects.EffectStackingRule.Stack
                    ? 1.0m + (effectMultiplier - 1.0m) * effect.Stacks
                    : effectMultiplier;
                multiplier *= stackMultiplier;
            }
        }

        return multiplier;
    }

    private void ApplyDamage(WorldState world, string targetId, int damage)
    {
        if (EntityId.IsCharacter(targetId))
        {
            var charId = EntityId.ToGuid(targetId);
            var character = world.GetCharacter(charId);
            if (character != null)
            {
                character.Vitals.Health = Math.Max(0, character.Vitals.Health - damage);

                if (damage > 0)
                {
                    BreakConcentration(world, charId);
                }
            }
        }
        else if (EntityId.IsMob(targetId))
        {
            var mob = world.GetMob(EntityId.ToGuid(targetId));
            if (mob != null)
            {
                mob.Vitals.Health = Math.Max(0, mob.Vitals.Health - damage);
            }
        }
    }

    /// <summary>
    /// A blow breaks a cast. This is the only thing that interrupts a spell in a fight now -
    /// merely being in combat no longer does, or nobody could cast in one.
    /// </summary>
    private void BreakConcentration(WorldState world, Guid characterId)
    {
        var cancelled = world.CastQueue.CancelFor(characterId);
        if (cancelled.Count == 0)
        {
            return;
        }

        var actor = world.FindByCharacter(characterId);
        if (actor is null)
        {
            return;
        }

        foreach (var cast in cancelled)
        {
            actor.SendText(CastQueueService.InterruptedText(cast.AbilityKey), "ability");
            if (logger != null)
            {
                EngineLog.AbilityCastInterrupted(logger, actor.Name, cast.AbilityKey);
            }
        }
    }

    private static bool IsCombatActive(Combat combat) =>
        // Combat is active if there are at least 2 combatants (allows PvP and PvE)
        combat.Combatants.Count >= 2;

    private void HandleDeath(WorldState world, Combat combat, string entityId)
    {
        if (EntityId.IsCharacter(entityId))
        {
            var character = world.GetCharacter(EntityId.ToGuid(entityId));
            if (character != null)
            {
                HandleCharacterDeath(world, combat, character, entityId);
            }
        }
        else if (EntityId.IsMob(entityId))
        {
            var mob = world.GetMob(EntityId.ToGuid(entityId));
            if (mob != null)
            {
                HandleMobDeath(world, combat, mob, entityId);
            }
        }

        // After the handler, which still needs the hate list to name a killer.
        combat.RemoveCombatant(entityId);
    }

    private void HandleCharacterDeath(WorldState world, Combat combat, Character character, string combatantId)
    {
        var actor = world.FindByCharacter(character.Id);
        if (actor == null)
        {
            return;
        }

        // Determine if this was a PvP kill
        var killerType = CombatantType.Mob;
        foreach (var other in combat.Combatants)
        {
            if (other == combatantId)
            {
                continue;
            }

            if (EntityId.IsCharacter(other))
            {
                killerType = CombatantType.Player;
                break;
            }
        }

        var isPvpDeath = killerType == CombatantType.Player;

        // Log PvP kill
        if (isPvpDeath && logger != null)
        {
            var killerName = "Unknown";
            if (combat.Combatants.FirstOrDefault(c => c != combatantId && EntityId.IsCharacter(c)) is var killerId && killerId != null)
            {
                var killCharId = EntityId.ToGuid(killerId);
                var killerActor = world.FindByCharacter(killCharId);
                if (killerActor != null)
                {
                    killerName = killerActor.Name;
                }
            }
            EngineLog.PvpKill(logger, killerName, actor.Name, combat.RoomKey.ToString());
        }

        // Apply XP loss (PLAN.md §4.12)
        if (character.Level >= options.XpLossMinLevel && (!isPvpDeath || options.PvpCostsXp))
        {
            var xpBand = XpProgression.XpForLevel(character.Level + 1) -
                         XpProgression.XpForLevel(character.Level);
            var xpLoss = (long)Math.Round(xpBand * options.XpLossPercent);
            var levelThreshold = XpProgression.XpForLevel(character.Level);
            character.Xp = Math.Max(levelThreshold, character.Xp - xpLoss);
        }

        // Resolve respawn location
        var respawnRoom = character.RespawnRoomKey ?? options.StartingRoom;
        if (world.FindRoom(respawnRoom) == null)
        {
            respawnRoom = options.StartingRoom;
        }

        // Move and reset vitals
        world.Move(actor, respawnRoom);
        character.Vitals.Health = Math.Max(1, (int)(character.Vitals.HealthMax * options.RespawnHealthPercent));
        character.Vitals.Focus = 0;
        character.Vitals.Stamina = 0;
        character.CombatState = CombatState.Idle;
        character.CurrentTarget = null;

        // Narrate at death location and respawn location
        var deathRoom = combat.RoomKey;
        foreach (var occupant in world.OccupantsOf(deathRoom))
        {
            if (occupant.CharacterId != character.Id)
            {
                occupant.SendText($"{actor.Name} falls.", "death");
            }
        }
        actor.SendText($"You died. Respawned at {respawnRoom}.", "death");
        foreach (var occupant in world.OccupantsOf(respawnRoom))
        {
            if (occupant.CharacterId != character.Id)
            {
                occupant.SendText($"{actor.Name} appears.", "arrival");
            }
        }
    }

    private void HandleMobDeath(WorldState world, Combat combat, Mob mob, string combatantId)
    {
        // Find the top damager (killer)
        var killerId = combat.GetTopHater(combatantId);

        // Extract killer's character if it's a player
        Character? killerChar = null;
        if (killerId != null && EntityId.IsCharacter(killerId))
        {
            killerChar = world.GetCharacter(EntityId.ToGuid(killerId));
        }

        // Award XP to killer
        if (killerChar != null)
        {
            killerChar.Xp += mob.ResolvedXp;

            // Level up loop
            while (CharacterProgression.TryLevelUp(
                killerChar.Level, killerChar.Xp, killerChar.Attributes, killerChar.Path, killerChar.Vitals) is var result && result != null)
            {
                killerChar.Level = result.NewLevel;
                killerChar.Attributes = result.NewAttributes;
                killerChar.Vitals = result.NewVitals;
                var actor = world.FindByCharacter(killerChar.Id);
                if (actor != null)
                {
                    actor.SendText($"You advance to level {result.NewLevel}!", "levelup");
                }
            }

            // Award gold
            killerChar.Gold += mob.ResolvedGold;
        }

        var mobRoomKey = RoomKey.Parse(mob.RoomKey);
        RollLoot(world, mob, mobRoomKey);

        // Narrate mob death to the room
        var deathProse = NarrationHelper.BuildSentence(DisplayNameOf(mob), "falls.");
        foreach (var occupant in world.OccupantsOf(mobRoomKey))
        {
            occupant.SendText(deathProse, "death");
        }

        // Remove mob from world
        world.RemoveMob(mob);

        // Refresh the room for all players to see the mob removed and any loot spawned
        view?.RefreshRoom(world, mobRoomKey);
    }

    /// <summary>
    /// Rolls the dead mob's loot table onto the floor. Reads the template caches rather than the
    /// repositories, which is what lets this run on the loop thread - and means a builder's edit
    /// to a loot table takes effect without a restart.
    /// </summary>
    private void RollLoot(WorldState world, Mob mob, RoomKey roomKey)
    {
        if (mobTemplates is null || itemTemplates is null || itemSpawner is null)
        {
            return;
        }

        if (world.FindRoom(roomKey) is not { } room ||
            world.FindZone(room.ZoneKey) is not { } zone ||
            world.FindWorld(zone.WorldKey) is not { } worldEntity)
        {
            return;
        }

        if (mobTemplates.Get(mob.TemplateKey)?.Loot is not { } loot)
        {
            return;
        }

        foreach (var lootEntry in loot)
        {
            if (!lootEntry.TryGetValue("itemTemplateKey", out var itemKeyObj))
            {
                continue;
            }

            var itemKey = itemKeyObj?.ToString();
            if (string.IsNullOrEmpty(itemKey))
            {
                continue;
            }

            if (!lootEntry.TryGetValue("chance", out var chanceObj))
            {
                continue;
            }

            double chance = chanceObj switch
            {
                double d => d,
                float f => f,
                int i => i,
                decimal dec => (double)dec,
                System.Text.Json.JsonElement json when json.ValueKind == System.Text.Json.JsonValueKind.Number => json.GetDouble(),
                string s when double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0
            };

            if (world.Random.NextDouble() >= chance)
            {
                continue;
            }

            if (itemTemplates.Get(itemKey) is { } itemTemplate)
            {
                world.AddItem(itemSpawner.Spawn(itemTemplate, zone, worldEntity, roomKey));
            }
        }
    }

    private static void EndCombatFor(WorldState world, string combatantId)
    {
        if (EntityId.IsCharacter(combatantId))
        {
            var character = world.GetCharacter(EntityId.ToGuid(combatantId));
            if (character != null)
            {
                character.CombatState = CombatState.Idle;
                character.CurrentTarget = null;
            }
        }
        else if (EntityId.IsMob(combatantId))
        {
            var mob = world.GetMob(EntityId.ToGuid(combatantId));
            if (mob != null)
            {
                mob.CombatState = CombatState.Idle;
                mob.CurrentTarget = null;
            }
        }
    }

    /// <summary>Everything one exchange needs, resolved once rather than per swing.</summary>
    private sealed record StrikeContext(
        Combat Combat,
        string AttackerId,
        string TargetId,
        CombatantType AttackerType,
        string AttackerName,
        object? AttackerActor,
        CombatantType TargetType,
        string TargetName,
        object? TargetActor,
        TargetValidationResult Validation,
        long Pulse);
}
