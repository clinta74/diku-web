using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
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
    ILogger<AbilitySystem>? logger = null)
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
        var target = ResolveTarget(world, caster, ability, cast);

        if (effect is null || target is null)
        {
            actor.SendText(
                target is null
                    ? $"Your {ability.Name} finds nothing to take hold of."
                    : $"Your {ability.Name} fizzles.",
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

        effect.Apply(caster, target, ability.EffectParams, world.Random);

        // If this is a buff/debuff effect, also create the ongoing active effect state
        if (effect is IBuffEffect buffEffect)
        {
            var activeEffect = buffEffect.CreateActiveEffect(
                caster, target, ability.EffectParams, clock.CurrentPulse);
            var targetEntityId = target is Character c ? c.Id : ((Mob)target).Id;
            world.ApplyEffect(targetEntityId, activeEffect);
        }
    }

    /// <summary>
    /// What this cast lands on, or null when whatever it was aimed at is no longer there.
    /// </summary>
    /// <remarks>
    /// Resolved at the moment the cast lands, not when it was started: a cast time is a window in
    /// which the world can change, and the target dying inside it is the ordinary case rather
    /// than an edge one.
    /// </remarks>
    private static object? ResolveTarget(
        WorldState world,
        Character caster,
        Ability ability,
        CastJob cast)
    {
        if (ability.TargetingType == TargetingType.Self)
        {
            return caster;
        }

        if (ability.TargetingType != TargetingType.SingleTarget || string.IsNullOrEmpty(cast.TargetId))
        {
            // Includes Aoe, which nothing resolves yet - a catalogue test fails the build if any
            // ability declares it, so this cannot be reached from authored content.
            return null;
        }

        if (EntityId.IsCharacter(cast.TargetId))
        {
            return world.GetCharacter(EntityId.ToGuid(cast.TargetId));
        }

        return EntityId.IsMob(cast.TargetId)
            ? world.GetMob(EntityId.ToGuid(cast.TargetId))
            : null;
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

        return false;
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
