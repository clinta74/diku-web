using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Narration;
using DikuWeb.Engine.Quests;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// <c>eat</c> and <c>drink</c> — the first verbs in the game that consume an item.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both need a minimum length of three, and neither is arbitrary.</b> Directions register first
/// and win the prefix race: <c>e</c> and <c>ea</c> already resolve to <c>east</c>, and <c>d</c> and
/// <c>dr</c> to <c>down</c> and <c>drop</c>. <c>VerbReachabilityTests</c> asserts every verb is
/// reachable at its own abbreviation, and will say so loudly if either number moves.
/// </para>
/// <para>
/// <b>They consume, and until now nothing did.</b> <c>RoomExit.RequiredItemKey</c> says out loud
/// that a keyed exit checks the pack and never takes anything, and that was true of the whole game.
/// So an item that is both a key and a drink can now be swallowed — which is a content question
/// rather than a code one, and the answer is to author the drink separately from the container.
/// </para>
/// </remarks>
public static class NutritionCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        commands.Add(new CommandDefinition(
            "eat", 3, "eat <item> - eat something, if it is food", Eat));

        commands.Add(new CommandDefinition(
            "drink", 3, "drink <item> - drink something, if it is drink", Drink));
    }

    private static void Eat(CommandContext ctx) => Consume(
        ctx,
        verb: "eat",
        past: "eat",
        refusal: "is not food",
        valueOf: t => t.FoodValue,
        answer: (vitals, amount) => vitals.Hunger = Needs.Reduced(vitals.Hunger, amount),
        alreadyFull: vitals => vitals.Hunger == 0,
        nothingLeft: "You could not manage another bite.");

    private static void Drink(CommandContext ctx) => Consume(
        ctx,
        verb: "drink",
        past: "drink",
        refusal: "is not something you can drink",
        valueOf: t => t.DrinkValue,
        answer: (vitals, amount) => vitals.Thirst = Needs.Reduced(vitals.Thirst, amount),
        alreadyFull: vitals => vitals.Thirst == 0,
        nothingLeft: "You are not thirsty.");

    /// <summary>
    /// The shared half of both verbs: find it, check it, take it, say so.
    /// </summary>
    /// <remarks>
    /// One method because the two differ in four strings and a field. Two near-identical copies is
    /// how one of them quietly stops checking quest bindings a year from now.
    /// </remarks>
    private static void Consume(
        CommandContext ctx,
        string verb,
        string past,
        string refusal,
        Func<ItemTemplate, int?> valueOf,
        Action<Vitals, int> answer,
        Func<Vitals, bool> alreadyFull,
        string nothingLeft)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply($"{char.ToUpperInvariant(verb[0])}{verb[1..]} what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var item = NameMatch.Best(inventory, ctx.Argument, i => i.TemplateName, i => i.TemplateKey);

        if (item is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(item.DisplayName);

        // Read from the template, not the instance: an ItemInstance carries only its TemplateKey,
        // and the nourishment is the template's - so a builder who makes bread more filling makes
        // every loaf already baked more filling too.
        var template = ctx.ItemTemplates?.Get(item.TemplateKey);

        if (template is null || valueOf(template) is not { } value || value <= 0)
        {
            ctx.Reply($"{Capitalise(article)} {refusal}.", "bad");
            return;
        }

        // The same two guards destroy applies, and for the same reasons: something worn is not in
        // your hands, and a quest item eaten is progress that cannot be recovered.
        if (item.EquippedSlot is not null)
        {
            ctx.Reply($"You'll have to remove {article} first.", "bad");
            return;
        }

        if (QuestBinding.RefuseDestroy(
                ctx.Quests, ctx.World, ctx.Actor.CharacterId, item, article) is { } bound)
        {
            ctx.Reply(bound, "bad");
            return;
        }

        var vitals = ctx.Actor.Character.Vitals;

        // Refused rather than wasted. Taking the item and giving nothing back is the shape of a bug
        // even when it is the rule, and the player cannot see the number they are already at.
        if (alreadyFull(vitals))
        {
            ctx.Reply(nothingLeft, "bad");
            return;
        }

        answer(vitals, value);

        // Out of the world and out of storage. Removing it in memory alone hands it back on the
        // next load - the comment destroy carries, for the same reason.
        ctx.World.RemoveItem(item);
        ctx.ItemSaveQueue?.EnqueueDelete(item.Id);

        ctx.Reply($"You {past} {article}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} {past}s {article}.", "movement");

        var remaining = verb == "eat"
            ? Needs.DescribeHunger(vitals.Hunger)
            : Needs.DescribeThirst(vitals.Thirst);

        // Said only when something is still wrong, so a meal that fixed it ends on the meal.
        if (remaining is not null)
        {
            ctx.Reply($"You are still {remaining}.", "bad");
        }
    }

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
