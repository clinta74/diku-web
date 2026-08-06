using DikuWeb.Engine.Protocol;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Status and effect-related commands: buffs, debuffs, etc.
/// </summary>
public static class StatusCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "buffs", 1, "buffs - list your active effects", ListBuffs));
    }

    private static void ListBuffs(CommandContext ctx)
    {
        var effects = ctx.World.GetActiveEffects(ctx.Actor.Character.Id);

        if (effects.Count == 0)
        {
            ctx.Reply("You have no active effects.");
            return;
        }

        var spans = new List<TextSpan> { new("Active Effects:", "room-title") };

        foreach (var effect in effects.OrderBy(e => e.ExpiresAtPulse))
        {
            var stackInfo = effect.MaxStacks > 1 ? $" ({effect.Stacks}/{effect.MaxStacks})" : "";
            var line = $"\n  {effect.Name}{stackInfo}";
            spans.Add(new TextSpan(line, effect.OutgoingDamageMultiplier > 1.0m ? "good" :
                                         effect.IncomingDamageMultiplier > 1.0m ? "bad" : "text"));
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }
}
