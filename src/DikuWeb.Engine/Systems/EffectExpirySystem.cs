using DikuWeb.Domain.Entities;
using DikuWeb.Engine.World;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Runs on the 60s tick to expire active effects and narrate their end.
/// Mirrors RegenSystem's shape.
/// </summary>
public static class EffectExpirySystem
{
    public static void Tick(WorldState world, long currentPulse)
    {
        var expired = world.ExpireEffects(currentPulse);

        foreach (var effect in expired)
        {
            // If the affected entity is a character in the world, narrate the expiry
            if (EntityId.IsCharacter(effect.SourceEntityId))
            {
                var charId = EntityId.ToGuid(effect.SourceEntityId);
                var character = world.GetCharacter(charId);
                if (character != null)
                {
                    var actor = world.FindByCharacter(character.Id);
                    if (actor != null)
                    {
                        actor.SendText($"Your {effect.Name} fades.", "ability");
                    }
                }
            }
            // Mobs get no narration on expiry (silent fade)
        }
    }
}
