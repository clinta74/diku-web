using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Ends effects whose time is up, and tells the bearer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bearer, not the caster.</b> This read <c>SourceEntityId</c> — the only id a bare
/// <c>ActiveEffect</c> carries — as though it were the entity the effect was sitting on. That is
/// true of a self-buff and false of every debuff: a Warden who landed Sunder on a mob was told
/// "Your sundered fades" when it ran out, about an effect that was never on them.
/// </para>
/// <para>
/// <b>Every pulse, not every minute.</b> It ran on the regen tick, so a twenty-second debuff was
/// announced up to a minute after it ended — and since the fight was usually over by then, the
/// line arrived attached to nothing. The mechanics were never late: combat re-reads
/// <c>ExpiresAtPulse</c> on every pulse and stops applying an expired effect on time. It was only
/// the message. Walking the table is cheap and the common case is that it is empty.
/// </para>
/// </remarks>
public static class EffectExpirySystem
{
    public static void Tick(WorldState world, long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(world);

        foreach (var (entityId, effect) in world.ExpireEffects(currentPulse))
        {
            // A mob's effects end in silence. Nobody is reading its status screen, and the player
            // who applied it is told by the fight rather than by a system message.
            if (world.FindByCharacter(entityId) is { } actor)
            {
                actor.SendText($"You are no longer {effect.Name}.", "ability");
            }
        }
    }
}
