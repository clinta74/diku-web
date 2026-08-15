using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Systems;

namespace DikuWeb.Engine.Commands;

public static class CombatCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        // "attack" rather than "kill", because the word the game reaches for first sets its tone
        // and there is no mechanical reason for that word to be the harsher one - the help text
        // already described kill as "attack target".
        //
        // Three characters, not one: "a" and "ab" already belong to `abilities`, and taking them
        // would shadow it. "att" is unambiguous and there is nothing else in the table it could be.
        commands.Add(new CommandDefinition(
            "attack", 3, "attack <target> (att) - attack target", Attack));

        // The old spelling, kept working and kept out of help.
        //
        // <b>Deleting it would have cost `k`</b>, which is thirty years of muscle memory in this
        // genre and the single most typed verb in a fight. Renaming a verb is a tone change; taking
        // away the keystroke people use without looking is a usability change, and only the first
        // one was asked for. MinLength stays at 1 so `k` still lands where it always has.
        commands.Add(new CommandDefinition(
            "kill", 1, "kill <target> (k) - attack target", Attack, Hidden: true));

        commands.Add(new CommandDefinition(
            "consider", 1, "consider <target> (con) - estimate target strength", Consider));

        commands.Add(new CommandDefinition(
            "flee", 1, "flee (f) - attempt to escape combat", Flee));

        commands.Add(new CommandDefinition(
            "bind", 0, "bind (b) - set respawn point in this room", Bind));
    }

    private static void Attack(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Attack what?");
            return;
        }

        var targetName = ctx.Argument;
        var actor = ctx.Actor;
        var character = actor.Character;

        // Before the target is even resolved: what you can see from a bedroll is not the question,
        // and "you don't see 'rat' here" would be the wrong answer to "you are asleep".
        if (RestGate.Refuse(character) is { } resting)
        {
            ctx.Reply(resting, "bad");
            return;
        }

        // Find target in room
        var targetActor = ctx.World.OthersIn(character.RoomKey, actor)
            .FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));

        var targetMob = NameMatch.Best(
            ctx.World.MobsIn(character.RoomKey), targetName, m => m.TemplateName, m => m.TemplateKey);

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

        // Validate target before entering combat. Shared with `cast`, so a rule that refuses a
        // swing refuses a spell too - they used to disagree, and the spell was the permissive one.
        var refusal = targetActor != null
            ? HostileActionGate.RefusePlayer(
                ctx.World, character.RoomKey, character.Id, targetActor.CharacterId, targetActor.Name)
            : HostileActionGate.RefuseMob(ctx.World, ctx.MobTemplates, character.RoomKey, targetMob!);

        if (refusal is not null)
        {
            ctx.Reply(refusal, "bad");
            return;
        }

        // Get or create combat for this room
        var combat = ctx.World.GetOrCreateCombat(character.RoomKey);

        // Enter combat
        if (targetActor != null)
        {
            var targetId = EntityId.ForCharacter(targetActor.CharacterId);
            character.CombatState = CombatState.Fighting;
            character.CurrentTarget = targetId;
            combat.AddCombatant(EntityId.ForCharacter(character.Id));
            combat.AddCombatant(targetId);
            combat.PlayerTargets[character.Id] = targetId;

            ctx.Reply($"You begin attacking {targetActor.Name}!");
            ctx.Broadcast($"{actor.Name} attacks {targetActor.Name}!");
            targetActor.Character.CombatState = CombatState.Fighting;
        }
        else if (targetMob != null)
        {
            var displayName = string.IsNullOrEmpty(targetMob.TemplateName) ? targetMob.TemplateKey : targetMob.TemplateName;

            // The six steps of starting a fight live in one place, because three things now do it
            // - this verb, a taunt, and a damaging ability landing on something not yet engaged.
            CombatEngagement.Engage(ctx.World, character, targetMob);

            ctx.Reply($"You begin attacking {NarrationHelper.WithArticle(displayName)}!");
            ctx.Broadcast($"{actor.Name} attacks {NarrationHelper.WithArticle(displayName)}!");
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

        var targetMob = NameMatch.Best(
            ctx.World.MobsIn(actor.Character.RoomKey), targetName, m => m.TemplateName, m => m.TemplateKey);

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
            // The effective level, not the template's (§4.7). A rat lifted to level 40 by its
            // zone's band is a level 40 fight and pays like one, so reporting "Level 1" here would
            // be the game warning you about a different mob from the one it is about to reward you
            // for. `examine` is the builder's view and still shows what was authored.
            var level = ctx.World.EffectiveLevelOf(targetMob);
            var name = string.IsNullOrEmpty(targetMob.TemplateName)
                ? targetMob.TemplateKey
                : targetMob.TemplateName;

            ctx.Reply(
                $"{NarrationHelper.WithArticle(name, capitalize: true)} — Level {level}. " +
                Assess(actor.Character.Level, level));
        }
    }

    /// <summary>
    /// How a fight looks, from the same numbers that decide what it pays (PLAN.md §4.7).
    /// </summary>
    /// <remarks>
    /// <b>Above your level is unchanged</b>, and stays on absolute level differences. Danger does
    /// not scale with your level the way relevance does: a mob five levels above you is a hard
    /// fight at level 10 and a hard fight at level 50, and there is nothing to make proportional.
    ///
    /// <b>Below your level is the reward window</b>, because the two were quietly contradicting
    /// each other. `consider` used a fixed ±5 band while experience uses half your level, so at
    /// level 50 a level 44 mob read <em>"You are much stronger"</em> and then paid 77% of full
    /// experience — the warning and the reward describing different fights. At level 10 the two
    /// happened to agree, which is exactly why nobody noticed.
    ///
    /// The bottom tier is the one that earns its place: a mob that will teach you nothing says so
    /// outright, so the answer to "is this worth my time" is available before the fight instead of
    /// after it.
    /// </remarks>
    private static string Assess(int viewerLevel, int targetLevel)
    {
        var difference = viewerLevel - targetLevel;

        if (difference <= 0)
        {
            return difference switch
            {
                > -2 => "It looks evenly matched.",
                > -5 => "It looks stronger.",
                _ => "It looks much stronger.",
            };
        }

        return XpRelevance.Fraction(viewerLevel, targetLevel) switch
        {
            <= 0 => "There is nothing left for you to learn from it.",
            < 0.4 => "You are much stronger.",
            < 0.75 => "You are stronger.",
            _ => "It looks evenly matched.",
        };
    }

    private static void Flee(CommandContext ctx)
    {
        var character = ctx.Actor.Character;

        if (character.CombatState == CombatState.Idle)
        {
            ctx.Reply("You're not in combat.");
            return;
        }

        // A snare denies the escape, which is the whole of what it does: ordinary movement is
        // already refused while fighting, so a root that only blocked walking would do nothing
        // in the one situation it is ever cast in.
        if (ctx.World.IsRooted(character.Id, ctx.Clock?.CurrentPulse ?? 0L))
        {
            var holding = ctx.World.RootName(character.Id, ctx.Clock?.CurrentPulse ?? 0L) ?? "something";
            ctx.Reply($"You cannot break away — you are {holding}.", "bad");
            return;
        }

        // End combat for this character
        var combat = ctx.World.FindCombat(character.RoomKey);
        if (combat != null)
        {
            var combatantId = EntityId.ForCharacter(character.Id);

            // RemoveCombatant also purges this character from every mob's hate list. Without
            // that, a fled player stayed the mob's top hater and kept being hit while reading
            // that they had escaped.
            combat.RemoveCombatant(combatantId);
            character.CombatState = CombatState.Idle;
            character.CurrentTarget = null;

            // A fight of one is no fight. Leave the mobs idle rather than stuck Fighting, which
            // would keep them from ever engaging anyone again.
            if (combat.Combatants.Count < 2)
            {
                foreach (var remaining in combat.Combatants.Where(EntityId.IsMob))
                {
                    var mob = ctx.World.GetMob(EntityId.ToGuid(remaining));
                    if (mob != null)
                    {
                        mob.CombatState = CombatState.Idle;
                        mob.CurrentTarget = null;
                    }
                }
            }

            ctx.Reply("You manage to escape!");
            ctx.Broadcast($"{ctx.Actor.Name} flees from combat!");
        }
        else
        {
            // Shouldn't happen, but handle gracefully
            character.CombatState = CombatState.Idle;
            character.CurrentTarget = null;
            ctx.Reply("You're no longer in combat.");
        }
    }

    private static void Bind(CommandContext ctx)
    {
        var character = ctx.Actor.Character;

        // Refused mid-fight for the reason travel is (Travel.Refuse): where you wake up is not a
        // decision to be taking while being hit. Only that one clause carries over, though —
        // binding is not travel, so a `noRecall` room and a seated character are both fine here.
        if (character.CombatState == CombatState.Fighting)
        {
            ctx.Reply("Not while you are fighting. Try 'flee' first.", "bad");
            return;
        }

        // Check if room allows binding
        if (!ctx.World.IsFlagSet(character.RoomKey, RoomFlags.Respawn))
        {
            ctx.Reply("You cannot bind your soul in this place.");
            return;
        }

        var previous = character.RespawnRoomKey;

        if (previous is { } already && already == character.RoomKey)
        {
            ctx.Reply("Your soul is already bound here.");
            return;
        }

        character.RespawnRoomKey = character.RoomKey;

        // Says what it replaced. The verb is one keystroke and overwrites without asking, and
        // `quit` demands all four characters precisely because a stray keypress should not cost
        // you something you only discover the next time you die.
        ctx.Reply(previous is { } old
            ? $"You bind your soul to this place: {character.RoomKey} (unbound from {old})."
            : $"You bind your soul to this place: {character.RoomKey}");

        ctx.Broadcast($"{ctx.Actor.Name} binds their soul here.");
    }
}
