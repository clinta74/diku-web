using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Entities;
using DikuWeb.Engine.Abilities;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Ability-related commands: cast and list known abilities.
/// </summary>
public static class AbilityCommands
{
    public static void Register(List<CommandDefinition> commands, AbilityCache? abilityCache = null)
    {
        commands.Add(new CommandDefinition(
            "cast", 1, "cast <ability> [target] (c) - cast an ability",
            ctx => Cast(ctx, abilityCache)));

        commands.Add(new CommandDefinition(
            "abilities", 0, "abilities (ab) - list your known abilities", ListAbilities));
    }

    private static void Cast(CommandContext ctx, AbilityCache? cache)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Cast what ability?");
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var abilityNameOrKey = parts[0];
        var targetName = parts.Length > 1 ? parts[1] : null;

        var character = ctx.Actor.Character;

        // Determine which abilities the character knows (at their level)
        var knownAbilityKeys = AbilityProgression.GetKnownAbilitiesForLevel(character.Path, character.Level);
        if (!knownAbilityKeys.Any())
        {
            ctx.Reply("You don't know any abilities yet.");
            return;
        }

        // Find ability by key or name prefix
        var matchingKey = knownAbilityKeys.FirstOrDefault(k =>
            k.EndsWith(abilityNameOrKey, StringComparison.OrdinalIgnoreCase) ||
            k.Contains(abilityNameOrKey, StringComparison.OrdinalIgnoreCase));

        if (matchingKey == null)
        {
            ctx.Reply($"You don't know an ability called '{abilityNameOrKey}'.");
            return;
        }

        // Resolve ability template from cache
        var ability = cache?.Get(matchingKey);
        if (ability == null)
        {
            ctx.Reply($"Ability '{matchingKey}' not configured. (Server error)");
            return;
        }

        // Check cooldown
        var lastCastPulse = ctx.World.GetAbilityCooldown(character.Id, matchingKey);
        var currentPulse = 0L; // TODO: inject game clock to get current pulse
        var cooldownRemaining = (lastCastPulse + ability.CooldownPulses) - currentPulse;
        if (cooldownRemaining > 0)
        {
            var secondsRemaining = Math.Ceiling(cooldownRemaining * 0.25); // 250ms per pulse
            ctx.Reply($"{ability.Name} is on cooldown ({secondsRemaining}s remaining).");
            return;
        }

        // Check cost
        var currentResource = ability.CostType switch
        {
            CostType.Focus => character.Vitals.Focus,
            CostType.Stamina => character.Vitals.Stamina,
            CostType.Health => character.Vitals.Health,
            _ => 0,
        };

        if (currentResource < ability.CostValue)
        {
            ctx.Reply($"Not enough {ability.CostType.ToString().ToLower()} (need {ability.CostValue}, have {currentResource}).");
            return;
        }

        // Resolve target if specified
        string? targetId = null;
        if (!string.IsNullOrEmpty(targetName))
        {
            var targetActor = ctx.World.OthersIn(character.RoomKey, ctx.Actor)
                .FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));

            if (targetActor != null)
                targetId = EntityId.ForCharacter(targetActor.CharacterId);
        }

        // Enqueue cast
        var castJob = new CastJob
        {
            CharacterId = character.Id,
            AbilityKey = matchingKey,
            TargetId = targetId,
            ResolveAtPulse = 0, // Instant (would be currentPulse + ability.CastTimePulses otherwise)
            StartingRoomKey = character.RoomKey.ToString(),
        };

        ctx.World.CastQueue.Enqueue(castJob);

        // Narrate to player
        ctx.Reply($"You cast {matchingKey}!");

        // Narrate to room
        if (targetId != null)
        {
            var target = ctx.World.OthersIn(character.RoomKey, ctx.Actor)
                .FirstOrDefault(p => EntityId.ForCharacter(p.CharacterId) == targetId);
            if (target != null)
            {
                ctx.Broadcast($"{ctx.Actor.Name} casts {matchingKey} on {target.Name}!");
                target.SendText($"{ctx.Actor.Name} casts {matchingKey} on you!", "ability");
            }
        }
        else
        {
            ctx.Broadcast($"{ctx.Actor.Name} casts {matchingKey}!");
        }
    }

    private static void ListAbilities(CommandContext ctx)
    {
        var character = ctx.Actor.Character;
        var knownAbilities = AbilityProgression.GetKnownAbilitiesForLevel(character.Path, character.Level);

        if (!knownAbilities.Any())
        {
            ctx.Reply("You don't know any abilities yet.");
            return;
        }

        ctx.Reply($"Your abilities ({character.Path}):");
        foreach (var key in knownAbilities)
        {
            ctx.Reply($"  • {key}");
        }
    }
}
