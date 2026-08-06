using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;

namespace DikuWeb.Engine.Commands;

public static class CombatCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "kill", 1, "kill <target> (k) - attack target", Kill));

        commands.Add(new CommandDefinition(
            "consider", 1, "consider <target> (con) - estimate target strength", Consider));

        commands.Add(new CommandDefinition(
            "flee", 1, "flee (f) - attempt to escape combat", Flee));
    }

    private static void Kill(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Kill what?");
            return;
        }

        var targetName = ctx.Argument;
        var actor = ctx.Actor;
        var character = actor.Character;

        // Find target in room
        var targetActor = ctx.World.OthersIn(character.RoomKey, actor)
            .FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));

        var targetMob = ctx.World.MobsIn(character.RoomKey)
            .FirstOrDefault(m => m.TemplateKey.EndsWith(targetName, StringComparison.OrdinalIgnoreCase));

        if (targetActor == null && targetMob == null)
        {
            ctx.Reply($"You don't see '{targetName}' here.");
            return;
        }

        // Check if already in combat with a different target
        if (character.CombatState == CombatState.Fighting && character.CurrentTarget != null)
        {
            ctx.Reply("You're already in combat!");
            return;
        }

        // Get or create combat for this room
        var combat = ctx.World.GetOrCreateCombat(character.RoomKey);

        // Enter combat
        if (targetActor != null)
        {
            var targetId = $"c_{targetActor.CharacterId}";
            character.CombatState = CombatState.Fighting;
            character.CurrentTarget = targetId;
            combat.AddCombatant($"c_{character.Id}");
            combat.AddCombatant(targetId);

            ctx.Reply($"You begin attacking {targetActor.Name}!");
            ctx.Broadcast($"{actor.Name} attacks {targetActor.Name}!");
            targetActor.Character.CombatState = CombatState.Fighting;
        }
        else if (targetMob != null)
        {
            var targetId = $"m_{targetMob.Id}";
            character.CombatState = CombatState.Fighting;
            character.CurrentTarget = targetId;
            combat.AddCombatant($"c_{character.Id}");
            combat.AddCombatant(targetId);

            ctx.Reply($"You begin attacking {targetMob.TemplateKey}!");
            ctx.Broadcast($"{actor.Name} attacks a {targetMob.TemplateKey}!");
            targetMob.CombatState = CombatState.Fighting;
        }
    }

    private static void Consider(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Consider what?");
            return;
        }

        var targetName = ctx.Argument;
        var actor = ctx.Actor;

        // Find target
        var targetActor = ctx.World.OthersIn(actor.Character.RoomKey, actor)
            .FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));

        var targetMob = ctx.World.MobsIn(actor.Character.RoomKey)
            .FirstOrDefault(m => m.TemplateKey.EndsWith(targetName, StringComparison.OrdinalIgnoreCase));

        if (targetActor == null && targetMob == null)
        {
            ctx.Reply($"You don't see '{targetName}' here.");
            return;
        }

        if (targetActor != null)
        {
            var targetChar = targetActor.Character;
            var levelDiff = actor.Character.Level - targetChar.Level;

            var assessment = levelDiff switch
            {
                > 5 => "You are much stronger.",
                > 2 => "You are stronger.",
                > -2 => "You are evenly matched.",
                > -5 => "They are stronger.",
                _ => "They are much stronger.",
            };

            ctx.Reply($"{targetActor.Name} — Level {targetChar.Level} {targetChar.Path}. {assessment}");
        }
        else if (targetMob != null)
        {
            var levelDiff = actor.Character.Level - targetMob.Level;

            var assessment = levelDiff switch
            {
                > 5 => "You are much stronger.",
                > 2 => "You are stronger.",
                > -2 => "It looks evenly matched.",
                > -5 => "It looks stronger.",
                _ => "It looks much stronger.",
            };

            ctx.Reply($"A {targetMob.TemplateKey} — Level {targetMob.Level}. {assessment}");
        }
    }

    private static void Flee(CommandContext ctx)
    {
        var character = ctx.Actor.Character;

        if (character.CombatState == CombatState.Idle)
        {
            ctx.Reply("You're not in combat.");
            return;
        }

        character.CombatState = CombatState.Fleeing;
        ctx.Reply("You attempt to flee!");
        ctx.Broadcast($"{ctx.Actor.Name} attempts to flee!");
    }
}
