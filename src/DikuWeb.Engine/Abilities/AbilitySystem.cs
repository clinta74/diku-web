using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Engine.Abilities;

/// <summary>
/// Runs on each game loop tick. Resolves pending casts whose cast time has elapsed,
/// interrupts casts if the caster moved or entered combat.
/// TODO: Wire IAbilityRepository and EffectRegistry to apply effects when casts resolve
/// (currently effect application requires async repository access from sync tick context).
/// </summary>
public sealed class AbilitySystem(
    IGameClock clock,
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
            return;

        var actor = world.FindByCharacter(caster.Id);
        if (actor == null)
            return;

        if (logger != null)
            EngineLog.AbilityResolving(logger, actor.Name, cast.AbilityKey);

        // Narrate cast
        actor.SendText($"Your {cast.AbilityKey} takes effect!", "ability");
        foreach (var occupant in world.OccupantsOf(caster.RoomKey))
        {
            if (occupant.CharacterId != caster.Id)
                occupant.SendText($"{actor.Name}'s {cast.AbilityKey} takes effect!", "ability");
        }

        // TODO: Resolve target and apply effect asynchronously
        // For now, just narrate - effect application requires async repository access from sync context
    }

    private bool ShouldInterrupt(WorldState world, CastJob cast)
    {
        var caster = world.GetCharacter(cast.CharacterId);
        if (caster == null)
            return true; // Character gone = interrupt

        // If caster enters combat while casting, interrupt
        if (caster.CombatState == DikuWeb.Domain.Combat.CombatState.Fighting)
            return true;

        // If caster moved rooms, interrupt
        if (caster.RoomKey.ToString() != cast.StartingRoomKey)
            return true;

        return false;
    }

    private void InterruptCast(WorldState world, CastJob cast)
    {
        var caster = world.GetCharacter(cast.CharacterId);
        if (caster == null)
            return;

        var actor = world.FindByCharacter(caster.Id);
        if (actor == null)
            return;

        actor.SendText($"Your {cast.AbilityKey} was interrupted.", "ability");
        if (logger != null)
            EngineLog.AbilityCastInterrupted(logger, actor.Name, cast.AbilityKey);
    }
}
