using Muwbta.Domain.Characters;

namespace Muwbta.Engine.Commands;

public static class RestCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "sleep", 1, "sleep (sl) - rest deeply and regenerate quickly", Sleep));

        commands.Add(new CommandDefinition(
            "rest", 1, "rest (r) - sit and recover", Rest));

        commands.Add(new CommandDefinition(
            "stand", 2, "stand (st) - wake up and be ready", Stand));
    }

    private static void Sleep(CommandContext ctx)
    {
        if (ctx.Actor.Character.RestState == CharacterRestState.Sleep)
        {
            ctx.Reply("You are already asleep.");
            return;
        }

        ctx.Actor.Character.RestState = CharacterRestState.Sleep;
        ctx.Reply("You lie down and fall into a deep sleep.");
        ctx.Broadcast($"{ctx.Actor.Name} lies down and falls asleep.");
    }

    private static void Rest(CommandContext ctx)
    {
        if (ctx.Actor.Character.RestState == CharacterRestState.Rest)
        {
            ctx.Reply("You are already resting.");
            return;
        }

        ctx.Actor.Character.RestState = CharacterRestState.Rest;
        ctx.Reply("You sit down to rest and recover.");
        ctx.Broadcast($"{ctx.Actor.Name} sits down to rest.");
    }

    private static void Stand(CommandContext ctx)
    {
        if (ctx.Actor.Character.RestState == CharacterRestState.Stand)
        {
            ctx.Reply("You are already standing.");
            return;
        }

        ctx.Actor.Character.RestState = CharacterRestState.Stand;
        ctx.Reply("You stand up, ready for action.");
        ctx.Broadcast($"{ctx.Actor.Name} stands up.");
    }
}
