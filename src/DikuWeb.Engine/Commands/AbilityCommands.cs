using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
using DikuWeb.Engine.Abilities;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Ability-related commands: cast and list known abilities.
/// </summary>
public static class AbilityCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "cast", 1, "cast <ability> [target] (c) - cast an ability", Cast));

        commands.Add(new CommandDefinition(
            "abilities", 0, "abilities (ab) - list your known abilities", ListAbilities));
    }

    private static void Cast(CommandContext ctx)
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
        // For now, just check against the known keys (ability details would come from repo in real impl)
        var matchingKey = knownAbilityKeys.FirstOrDefault(k =>
            k.EndsWith(abilityNameOrKey, StringComparison.OrdinalIgnoreCase) ||
            k.Contains(abilityNameOrKey, StringComparison.OrdinalIgnoreCase));

        if (matchingKey == null)
        {
            ctx.Reply($"You don't know an ability called '{abilityNameOrKey}'.");
            return;
        }

        // TODO: Resolve ability from repository and apply effects
        // For now, just emit a placeholder message
        ctx.Reply($"You cast {matchingKey}!");

        // If target specified, try to find them
        if (!string.IsNullOrEmpty(targetName))
        {
            var targetActor = ctx.World.OthersIn(character.RoomKey, ctx.Actor)
                .FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));

            if (targetActor != null)
            {
                ctx.Broadcast($"{ctx.Actor.Name} casts {matchingKey} on {targetActor.Name}!");
                targetActor.SendText($"{ctx.Actor.Name} casts {matchingKey} on you!");
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
