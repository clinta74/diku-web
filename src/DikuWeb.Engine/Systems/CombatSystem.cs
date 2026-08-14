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
                // Kills this loop did not make, before anything acts on this pulse. Abilities
                // resolve earlier in the same pulse and write straight to the health bar, so a
                // mob killed by a kick used to sit at zero health inside the fight for ever: it
                // could not swing, so nothing ever ended the combat, and the experience, the loot
                // and the corpse never came. The player was left permanently Fighting and could
                // not even `kill` again.
                //
                // This does not weaken "the blow that kills ends the exchange here and now" -
                // Strike still resolves its own kill the instant it lands. Sweeping first is what
                // keeps the guarantee: whatever killed this combatant, it is out of the fight
                // before anyone takes a turn, so the dead never get one more.
                ResolveDeathsFromOutside(world, combat);

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
    /// Resolves any combatant that arrived at this pulse already dead.
    /// </summary>
    /// <remarks>
    /// An ability, a trap, or anything else that reaches <c>Vitals.Health</c> from outside this
    /// loop. <see cref="HandleDeath"/> is the same door a swing's kill goes through, so the
    /// experience, the loot, the corpse and the removal from the fight are identical however the
    /// killing damage arrived - which is the whole point of routing it here rather than letting
    /// each damage source grow its own half of a death.
    ///
    /// A combatant the world no longer has counts as dead too. <see cref="HandleDeath"/> finds
    /// nothing to award and removes the entry, which is the cleanup a despawn would otherwise
    /// leave behind.
    /// </remarks>
    private void ResolveDeathsFromOutside(WorldState world, Combat combat)
    {
        foreach (var combatantId in combat.Combatants.ToList())
        {
            if (!IsAlive(world, combatantId))
            {
                HandleDeath(world, combat, combatantId);
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

        // Two different departures, and they used to be one condition with one consequence:
        // whoever left, the *attacker* was removed. When the target was the one who left, that
        // took the wrong party out of the fight - and took them out without releasing them, so
        // they kept CombatState.Fighting and their target while no longer being in Combatants,
        // where the end-of-fight sweep would have found them. Stuck for the rest of the session:
        // every later `kill` refused with "You're already in combat!", every direction refused
        // with "You can't leave while in combat!". The only way out was logging in again.
        if (GetCombatantRoom(world, attackerId) != combat.RoomKey)
        {
            combat.RemoveCombatant(attackerId);
            EndCombatFor(world, attackerId);
            return;
        }

        if (GetCombatantRoom(world, targetId) != combat.RoomKey)
        {
            // The target is removed and released; the attacker stays in the fight for
            // IsCombatActive to judge at the end of the pulse. With nothing left to hit it ends
            // the fight and releases everybody, which is the same door every other ending uses.
            combat.RemoveCombatant(targetId);
            EndCombatFor(world, targetId);
            ForgetTarget(world, combat, targetId);
            NarrateTargetGone(world, attackerId, targetId);
            return;
        }

        var (attackerType, attackerName, attackerActor) = ResolveCombatantInfo(world, attackerId);
        var (targetType, targetName, targetActor) = ResolveCombatantInfo(world, targetId);
        if (attackerType is null || targetType is null)
        {
            return;
        }

        // Whether the fight is allowed at all is re-checked every tick, so clearing `pvp` on a
        // room ends a duel already under way (PLAN.md §4.11). Grouping up mid-duel ends it the
        // same way, for the same reason: the rule is about the state of things now, not about
        // what was true when the first blow landed.
        var peaceful = world.IsFlagSet(combat.RoomKey, RoomFlags.Peaceful);
        var pvp = world.IsFlagSet(combat.RoomKey, RoomFlags.Pvp);
        var grouped = EntityId.IsCharacter(attackerId) && EntityId.IsCharacter(targetId) &&
            world.Parties.SameParty(EntityId.ToGuid(attackerId), EntityId.ToGuid(targetId));

        var validation = TargetValidator.ValidateTarget(
            attackerType.Value, targetType.Value, targetName, peaceful, pvp, grouped);

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

    /// <summary>
    /// The defender's stats as equipment resolves them, with any active defence effects folded in.
    /// </summary>
    /// <remarks>
    /// One place, for both characters and mobs, which is what lets a defence buff work on either.
    /// Folded in here rather than inside <c>EquipmentResolver</c> because that is Domain and knows
    /// nothing about who currently has what running on them.
    /// </remarks>
    private static DefenderStats? ResolveDefenderStats(WorldState world, string targetId)
    {
        DefenderStats? stats = null;

        if (EntityId.IsCharacter(targetId))
        {
            var character = world.GetCharacter(EntityId.ToGuid(targetId));
            stats = character is null
                ? null
                : EquipmentResolver.ResolveDefenderStats(
                    character.Attributes.AgilityModifier,
                    [.. world.InventoryOf(character.Id).Where(i => i.EquippedSlot is not null)]);
        }
        else if (EntityId.IsMob(targetId))
        {
            var mob = world.GetMob(EntityId.ToGuid(targetId));
            stats = mob is null ? null : DamageCalculator.DefenderStatsFrom(mob);
        }

        if (stats is null)
        {
            return null;
        }

        var effects = world.GetActiveEffects(EntityId.ToGuid(targetId));
        var defense = 0;
        var armor = 0;

        foreach (var effect in effects)
        {
            defense += effect.DefenseRatingDelta;
            armor += effect.ArmorFlatDelta;
        }

        if (defense == 0 && armor == 0)
        {
            return stats;
        }

        // Floored at zero rather than allowed negative. A stripped guard should make somebody
        // easy to hit, not easier than a defenceless one - and a negative armour value would turn
        // the subtraction in the damage formula into a bonus.
        return stats with
        {
            DefenseRating = Math.Max(0, stats.DefenseRating + defense),
            ArmorFlat = Math.Max(0, stats.ArmorFlat + armor),
        };
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

    /// <summary>
    /// Whether anyone left in this fight still has someone to hit.
    /// </summary>
    /// <remarks>
    /// <b>Sides, not heads.</b> This used to be <c>Combatants.Count >= 2</c>, which is right for
    /// exactly one shape of fight — one player against one mob, where the mob dying leaves one
    /// combatant and ends it. <b>In a group it never ended.</b> Two players on one mob is three
    /// combatants; the mob dies, two remain, the count is still two, and both players were left
    /// permanently <c>Fighting</c> — refused every later <c>kill</c> and unable to walk out of the
    /// room. It scaled with the party: the bigger the group, the more people it stranded.
    ///
    /// A target that is still in the fight is the honest test, because it is the same thing
    /// <see cref="RunCombatant"/> asks before swinging: if nobody can name an opponent, nobody is
    /// going to swing, and a fight where nothing can happen is over. It reads correctly for every
    /// shape without enumerating them — a duel stays active because each duellist targets the
    /// other, a taunted player stays in because the mob's hate list still names them, and a party
    /// standing over a corpse falls out because <see cref="Combat.RemoveCombatant"/> already
    /// cleared every target that pointed at it.
    /// </remarks>
    private static bool IsCombatActive(Combat combat)
    {
        foreach (var combatantId in combat.Combatants)
        {
            var target = ResolveTargetOf(combat, combatantId);

            if (!string.IsNullOrEmpty(target) &&
                target != combatantId &&
                combat.Combatants.Contains(target))
            {
                return true;
            }
        }

        return false;
    }

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

        var mobRoomKey = RoomKey.Parse(mob.RoomKey);

        if (killerChar != null)
        {
            AwardKill(world, killerChar, mob, mobRoomKey);
        }

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
    /// Hands out a kill's experience and gold, split across the killer's party (PLAN.md §5.3).
    /// </summary>
    /// <remarks>
    /// <b>Who shares is decided by the room, not by the roster.</b> A party member in another zone
    /// gets nothing: they were not at the fight, and a group that could farm by scattering across
    /// the map would make the split a exploit rather than a convenience. The killer is always
    /// first, so an odd remainder goes to whoever actually landed the blow.
    ///
    /// The killer is the top of the hate list, which since threat accounting landed means the top
    /// of <em>all</em> damage - so the Adept who nuked from behind the Warden is credited, and the
    /// split then hands the Warden their share anyway.
    /// </remarks>
    private static void AwardKill(WorldState world, Character killer, Mob mob, RoomKey roomKey)
    {
        var sharers = new List<Character> { killer };

        foreach (var memberId in world.Parties.MembersOf(killer.Id))
        {
            if (memberId == killer.Id || world.FindByCharacter(memberId) is not { } member)
            {
                continue;
            }

            // Present means standing where it died. A member who fled the room a moment ago is
            // out, which is the same rule combat itself uses for staying in a fight (§4.2).
            if (member.RoomKey == roomKey)
            {
                sharers.Add(member.Character);
            }
        }

        var xp = RewardShare.Split(mob.ResolvedXp, sharers.Count);
        var gold = RewardShare.Split(mob.ResolvedGold, sharers.Count);

        for (var i = 0; i < sharers.Count; i++)
        {
            Award(world, sharers[i], xp[i], gold[i], shared: sharers.Count > 1);
        }
    }

    private static void Award(WorldState world, Character character, long xp, long gold, bool shared)
    {
        character.Xp += xp;
        character.Gold += gold;

        var actor = world.FindByCharacter(character.Id);

        if (shared && actor is not null)
        {
            // Said out loud, because a share that arrived silently looks like the reward shrank.
            actor.SendText($"Your share: {xp} experience, {gold} gold.", "party");
        }

        while (CharacterProgression.TryLevelUp(
            character.Level,
            character.Xp,
            character.Attributes,
            character.Path,
            character.Vitals) is { } result)
        {
            character.Level = result.NewLevel;
            character.Attributes = result.NewAttributes;
            character.Vitals = result.NewVitals;
            actor?.SendText($"You advance to level {result.NewLevel}!", "levelup");
        }
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

    /// <summary>
    /// Clears anyone's aim at a combatant who has left the fight.
    /// </summary>
    /// <remarks>
    /// <see cref="Combat.RemoveCombatant"/> already drops the fight's own <c>PlayerTargets</c>
    /// entries, but a character carries its target on <c>CurrentTarget</c> as well, and that is
    /// the copy <c>kill</c> reads. Left behind, it refuses the next fight with "You're already in
    /// combat!" even though the fight it names has forgotten them — the two records disagreeing is
    /// the whole failure, so both have to be cleared in the same breath.
    /// </remarks>
    private static void ForgetTarget(WorldState world, Combat combat, string departedId)
    {
        foreach (var combatantId in combat.Combatants)
        {
            if (EntityId.IsCharacter(combatantId))
            {
                if (world.GetCharacter(EntityId.ToGuid(combatantId)) is { } character &&
                    character.CurrentTarget == departedId)
                {
                    character.CurrentTarget = null;
                }
            }
            else if (EntityId.IsMob(combatantId))
            {
                if (world.GetMob(EntityId.ToGuid(combatantId)) is { } mob &&
                    mob.CurrentTarget == departedId)
                {
                    mob.CurrentTarget = null;
                }
            }
        }
    }

    /// <summary>
    /// Says that the thing you were fighting is no longer there.
    /// </summary>
    /// <remarks>
    /// Every other way a fight ends has words on it — a death says "A rat falls.", fleeing says
    /// "You manage to escape!", a refused target explains itself. This one was silent, and the
    /// silence is why being trapped in a fight nobody was in went unnoticed for so long: it was
    /// indistinguishable from the bug. The room already narrates the departure itself; what was
    /// missing is that the departure ended something.
    /// </remarks>
    private static void NarrateTargetGone(WorldState world, string attackerId, string targetId)
    {
        if (!EntityId.IsCharacter(attackerId) ||
            world.FindByCharacter(EntityId.ToGuid(attackerId)) is not { } actor)
        {
            return;
        }

        var (_, name, _) = ResolveCombatantInfo(world, targetId);

        actor.SendText(
            string.IsNullOrEmpty(name)
                ? "You stop fighting."
                : $"You stop fighting {NarrationHelper.WithArticle(name)}.",
            "combat");
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
