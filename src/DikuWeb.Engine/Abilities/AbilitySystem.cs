using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Engine.Abilities;

/// <summary>
/// Runs on each game loop tick. Resolves pending casts whose cast time has elapsed,
/// interrupts casts if the caster moved or entered combat, applies effects and costs.
/// </summary>
public sealed class AbilitySystem(
    IGameClock clock,
    AbilityCache? cache = null,
    EffectRegistry? effects = null,
    ILogger<AbilitySystem>? logger = null,
    MobTemplateCache? mobTemplates = null)
{
    public const long TickIntervalPulses = 1; // Every pulse, so cast-time resolution is smooth

    public void Tick(WorldState world)
    {
        var currentPulse = clock.CurrentPulse;
        var castQueue = world.CastQueue;

        // Get all casts that are ready to resolve
        var ready = castQueue.GetReadyToResolve(currentPulse).ToList();
        foreach (var cast in ready)
        {
            ResolveCast(world, cast);
            castQueue.Remove(cast);
        }

        // Check for interruption: casts broken if caster moved or entered combat
        var pending = castQueue.Pending.ToList();
        foreach (var cast in pending)
        {
            if (ShouldInterrupt(world, cast))
            {
                InterruptCast(world, cast);
                castQueue.Remove(cast);
            }
        }
    }

    private void ResolveCast(WorldState world, CastJob cast)
    {
        var caster = world.GetCharacter(cast.CharacterId);
        if (caster == null)
        {
            return;
        }

        var actor = world.FindByCharacter(caster.Id);
        if (actor == null)
        {
            return;
        }

        if (logger != null)
        {
            EngineLog.AbilityResolving(logger, actor.Name, cast.AbilityKey);
        }

        // Resolve ability from cache
        var ability = cache?.Get(cast.AbilityKey);
        if (ability == null)
        {
            return;
        }

        // Everything that can fail is resolved before anything is spent. This used to narrate
        // "takes effect!", deduct the cost, and start the cooldown, and only then look for a
        // target - so a rat that died during an eight-pulse Bolt cost the caster full price for
        // an effect that never ran, and told them it had worked.
        var effect = effects?.Get(ability.EffectKey);
        var targets = effect is null ? [] : ResolveTargets(world, caster, ability, cast, effect);

        if (effect is null || targets.Count == 0)
        {
            actor.SendText(
                effect is null
                    ? $"Your {ability.Name} fizzles."
                    : $"Your {ability.Name} finds nothing to take hold of.",
                "bad");
            return;
        }

        // Narrate cast
        actor.SendText($"Your {ability.Name} takes effect!", "ability");
        foreach (var occupant in world.OccupantsOf(caster.RoomKey))
        {
            if (occupant.CharacterId != caster.Id)
            {
                occupant.SendText($"{actor.Name}'s {ability.Name} takes effect!", "ability");
            }
        }

        // Deduct cost
        var costAmount = ability.CostValue;
        switch (ability.CostType)
        {
            case CostType.Focus:
                caster.Vitals.Focus = Math.Max(0, caster.Vitals.Focus - costAmount);
                break;
            case CostType.Stamina:
                caster.Vitals.Stamina = Math.Max(0, caster.Vitals.Stamina - costAmount);
                break;
            case CostType.Health:
                caster.Vitals.Health = Math.Max(0, caster.Vitals.Health - costAmount);
                break;
        }

        // Set cooldown
        world.SetAbilityCooldown(caster.Id, cast.AbilityKey, clock.CurrentPulse);

        // One cost and one cooldown however many it lands on - an area effect is paid for by the
        // cast, not by the target, which is the whole reason to bring one.
        foreach (var target in targets)
        {
            // Health before and after, rather than asking the executor how much it dealt. The
            // executors return void and each computes damage its own way; reading the wound is
            // the one measure that is true of all of them and cannot drift from what landed.
            var before = HealthOf(target);

            effect.Apply(caster, target, ability.EffectParams, world.Random);

            CreditThreat(world, caster, target, before);

            if (effect is IThreatEffect threatEffect)
            {
                Taunt(world, actor, caster, target, threatEffect, ability);
            }

            // If this is a buff/debuff effect, also create the ongoing active effect state
            if (effect is IBuffEffect buffEffect)
            {
                var activeEffect = buffEffect.CreateActiveEffect(
                    caster, target, ability.EffectParams, clock.CurrentPulse);
                var targetEntityId = target is Character c ? c.Id : ((Mob)target).Id;
                world.ApplyEffect(targetEntityId, activeEffect);

                // A stun interrupts, but that is handled by ShouldInterrupt on the next tick
                // rather than here: driving it from the *state* means any stun breaks a cast
                // however it arrived, where doing it at the point of application would only
                // cover stuns delivered by a player's cast.
            }
        }
    }

    /// <summary>
    /// Puts the caster at the top of a mob's hate list and turns it on them.
    /// </summary>
    /// <remarks>
    /// The lead is a fraction of the target's <em>maximum</em> health rather than its current,
    /// so a taunt on something nearly dead is not worth less than the same taunt at full health -
    /// which would make the ability weakest at exactly the moment a fight is most likely to go
    /// wrong.
    ///
    /// The mob is retargeted immediately rather than left for the next combat tick to work out.
    /// The tick would get there, but a taunt whose whole promise is "it hits me now" cannot
    /// afford to be a swing late, and the swing it would be late by is the one landing on the
    /// person the taunt was meant to save.
    /// </remarks>
    private static void Taunt(
        WorldState world,
        PlayerActor actor,
        Character caster,
        object target,
        IThreatEffect effect,
        Ability ability)
    {
        if (target is not Mob mob)
        {
            // Nothing else has a hate list. Taunting another player is not a thing the game has
            // a meaning for, and silently doing nothing is the honest answer.
            return;
        }

        var combat = CombatEngagement.Engage(world, caster, mob, retarget: false);

        var mobId = EntityId.ForMob(mob.Id);
        var casterId = EntityId.ForCharacter(caster.Id);

        var lead = (int)Math.Ceiling(
            Math.Max(1, mob.Vitals.HealthMax) * effect.LeadFraction(ability.EffectParams));

        combat.ForceTopHater(mobId, casterId, lead);

        mob.CurrentTarget = casterId;

        var displayName = string.IsNullOrEmpty(mob.TemplateName) ? mob.TemplateKey : mob.TemplateName;
        actor.SendText(
            $"{NarrationHelper.WithDefiniteArticle(displayName, capitalize: true)} turns on you!",
            "combat");

        foreach (var occupant in world.OccupantsOf(caster.RoomKey))
        {
            if (occupant.CharacterId != caster.Id)
            {
                occupant.SendText(
                    $"{NarrationHelper.WithDefiniteArticle(displayName, capitalize: true)} " +
                    $"turns on {actor.Name}!",
                    "combat");
            }
        }
    }

    private static int HealthOf(object target) => target switch
    {
        Character character => character.Vitals.Health,
        Mob mob => mob.Vitals.Health,
        _ => 0,
    };

    /// <summary>
    /// Puts what an ability just did on the mob's hate list, engaging it if it was not fighting.
    /// </summary>
    /// <remarks>
    /// Ability damage used to reach <c>Vitals.Health</c> and stop there, so it was worth no
    /// threat at all. That inverted the design of the hate list, which picks its target by
    /// cumulative damage: <b>the Adept, the Path built to deal the most damage in the game, was
    /// the only one that could never pull a mob off anyone.</b> Cataclysm could take most of a
    /// boss's health and leave it chewing on the Warden who had scratched it twice.
    ///
    /// Hurting something also starts the fight, the way swinging at it does. Without that an
    /// opening Bolt was free: the mob took the damage and stood there, because nothing had put it
    /// in combat, and the threat had nowhere to be recorded either.
    /// </remarks>
    private static void CreditThreat(WorldState world, Character caster, object target, int before)
    {
        if (target is not Mob mob)
        {
            return;
        }

        var damage = before - mob.Vitals.Health;
        if (damage <= 0)
        {
            return;
        }

        // Not a retarget: an area effect hits everything in the room, and swinging the caster's
        // own weapon round to the last thing the flames touched is not what they asked for.
        CombatEngagement.Engage(world, caster, mob, retarget: false);

        ThreatCredit.Credit(
            world,
            caster.RoomKey,
            EntityId.ForCharacter(caster.Id),
            EntityId.ForMob(mob.Id),
            damage);
    }

    /// <summary>
    /// Everything this cast lands on. Empty when whatever it was aimed at is no longer there, or
    /// when an area effect gathered nobody it is allowed to touch.
    /// </summary>
    /// <remarks>
    /// Resolved at the moment the cast lands, not when it was started: a cast time is a window in
    /// which the world can change, and the target dying inside it is the ordinary case rather
    /// than an edge one. For an area effect that window matters more, since the room can fill or
    /// empty while the words are being said.
    /// </remarks>
    private IReadOnlyList<object> ResolveTargets(
        WorldState world,
        Character caster,
        Ability ability,
        CastJob cast,
        IAbilityEffect effect)
    {
        switch (ability.TargetingType)
        {
            case TargetingType.Self:
                return [caster];

            case TargetingType.Aoe:
                return AreaTargets(world, caster, effect.IsHarmful);

            case TargetingType.SingleTarget when !string.IsNullOrEmpty(cast.TargetId):
                object? one = EntityId.IsCharacter(cast.TargetId)
                    ? world.GetCharacter(EntityId.ToGuid(cast.TargetId))
                    : EntityId.IsMob(cast.TargetId)
                        ? world.GetMob(EntityId.ToGuid(cast.TargetId))
                        : null;

                return one is null ? [] : [one];

            default:
                return [];
        }
    }

    /// <summary>
    /// Everyone in the caster's room this effect is allowed to reach.
    /// </summary>
    /// <remarks>
    /// <b>Filtered per target, never per room</b> (PLAN.md §4.11). Asking once whether the room
    /// permits the cast and then hitting everything in it would make a single flag the difference
    /// between a spell and a massacre - the room a hostile AoE is cast in is routinely mixed, with
    /// mobs to kill and players who must not be touched standing in it.
    ///
    /// So the two directions gather different sets:
    /// <list type="bullet">
    /// <item><description><b>Harmful</b> - every mob that may be fought, plus other players only
    /// where the room resolves <c>pvp</c>. Nothing at all in a <c>peaceful</c> room, and never
    /// the caster.</description></item>
    /// <item><description><b>Helpful</b> - the caster, plus their party where they have one and
    /// everyone standing with them where they do not. Mobs are left out: there is no way yet to
    /// have an ally who is one.</description></item>
    /// </list>
    ///
    /// <b>Party membership beats both flags.</b> A harmful area effect skips the caster's group
    /// even in a <c>pvp</c> room - which is the room where it matters, since a duelling ground is
    /// exactly where a group fights alongside strangers. Until 5.3 this was approximated by the
    /// <c>pvp</c> flag alone, which meant the one place PvP was permitted was the one place your
    /// own Firestorm could kill the people you came in with.
    ///
    /// A helpful effect prefers the party over the room for the mirror-image reason: with a group
    /// present, "your side" is a thing the world can actually answer, and healing the strangers in
    /// the room instead of the group you are healing for is the wrong answer even though it is a
    /// generous one. Ungrouped, the room stays the approximation - it errs toward helping people,
    /// which is the safe direction.
    /// </remarks>
    private IReadOnlyList<object> AreaTargets(WorldState world, Character caster, bool harmful)
    {
        var room = caster.RoomKey;

        if (!harmful)
        {
            // Party members are gathered from the room, not from the roster: combat and healing
            // are room-local (§4.2), and a heal that reached a member two zones away would be a
            // different ability from the one the catalogue describes.
            var allies = world.OccupantsOf(room)
                .Where(p => p.CharacterId != caster.Id)
                .Where(p => !world.Parties.IsGrouped(caster.Id) ||
                            world.Parties.SameParty(caster.Id, p.CharacterId));

            return [caster, .. allies.Select(object (p) => p.Character)];
        }

        var targets = new List<object>();

        foreach (var mob in world.MobsIn(room))
        {
            // Through the same gate a swing and a single-target cast go through, so "peaceful
            // forbids it" and "an NPC is not someone you can fight" are one rule with one place
            // to change rather than three copies that agreed when they were written.
            if (HostileActionGate.MayStrikeMob(world, mobTemplates, room, mob))
            {
                targets.Add(mob);
            }
        }

        if (world.IsFlagSet(room, RoomFlags.Pvp))
        {
            targets.AddRange(world.OccupantsOf(room)
                .Where(p => p.CharacterId != caster.Id)
                .Where(p => !world.Parties.SameParty(caster.Id, p.CharacterId))
                .Select(object (p) => p.Character));
        }

        return targets;
    }

    private bool ShouldInterrupt(WorldState world, CastJob cast)
    {
        var caster = world.GetCharacter(cast.CharacterId);
        if (caster == null)
        {
            return true; // Character gone = interrupt
        }

        // Being in a fight is deliberately not an interrupt. It used to be, which made casting
        // in combat impossible - and made "melee pauses while you cast" unreachable, since a
        // fighting character could never be mid-cast. What breaks concentration is being hit,
        // and CombatSystem cancels the cast when damage lands.

        // If caster moved rooms, interrupt
        if (caster.RoomKey.ToString() != cast.StartingRoomKey)
        {
            return true;
        }

        // Stunned casters lose the thread. Checked here rather than at the moment a stun ability
        // resolves, so it holds however the stun arrived - a mob ability, a trap, or anything
        // else that ever applies one. Tying it to the casting path would have meant only stuns
        // delivered by a player cast could interrupt.
        return world.IsStunned(cast.CharacterId, clock.CurrentPulse);
    }

    private void InterruptCast(WorldState world, CastJob cast)
    {
        var caster = world.GetCharacter(cast.CharacterId);
        if (caster == null)
        {
            return;
        }

        var actor = world.FindByCharacter(caster.Id);
        if (actor == null)
        {
            return;
        }

        actor.SendText(CastQueueService.InterruptedText(cast.AbilityKey), "ability");
        if (logger != null)
        {
            EngineLog.AbilityCastInterrupted(logger, actor.Name, cast.AbilityKey);
        }
    }
}
