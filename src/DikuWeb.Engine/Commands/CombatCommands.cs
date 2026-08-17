using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.World;

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

        // Two characters, not one: "a" and "ab" belong to `abilities`, which sits at MinLength 0.
        commands.Add(new CommandDefinition(
            "assist", 2, "assist <player> (as) - attack whatever they are attacking", Assist));

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

        // Through NameMatch, like every other targeting verb. These five - attack, assist,
        // consider, cast's target, and autofollow - were exact-match on the name while tell, group
        // invite, group kick and every admin verb ranked prefixes, so `tell kae hello` worked and
        // `attack kae` answered "You don't see 'kae' here" (BUGS.md #21).
        var targetActor = NameMatch.Best(
            ctx.World.OthersIn(character.RoomKey, actor), targetName, p => p.Name, _ => null);

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

        if (targetActor != null)
        {
            EngagePlayer(ctx, targetActor);
        }
        else if (targetMob != null)
        {
            EngageMob(ctx, targetMob);
        }
    }

    /// <summary>
    /// Attacks whatever another player in this room is attacking.
    /// </summary>
    /// <remarks>
    /// <b>It exists because names cannot aim.</b> Two mobs of one kind in a room answer to the same
    /// word, and <see cref="NameMatch"/> breaks that tie on arrival order — which has nothing to do
    /// with which one your party is fighting. <see cref="MobLabel"/> makes the difference
    /// <em>visible</em> and <c>attack crow 2</c> makes it typeable, but neither makes helping
    /// somebody reliable: the ordinal shifts when one of them dies, and reading it off the screen
    /// mid-fight is not a thing anyone does. Naming the person is stable, because the person is who
    /// you actually meant.
    ///
    /// <b>It bypasses the target search entirely</b> rather than resolving a name near them. The
    /// whole point is to inherit a target that has already been decided, so there is nothing left
    /// to disambiguate and no way for this to land on a different mob than the one it copied.
    ///
    /// Refuses mid-fight exactly as <c>attack</c> does. Switching targets is a separate decision
    /// from choosing one, and this verb is for joining a fight rather than for changing your mind
    /// inside one.
    /// </remarks>
    private static void Assist(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Assist whom?");
            return;
        }

        var actor = ctx.Actor;
        var character = actor.Character;

        if (RestGate.Refuse(character) is { } resting)
        {
            ctx.Reply(resting, "bad");
            return;
        }

        var ally = NameMatch.Best(
            ctx.World.OthersIn(character.RoomKey, actor), ctx.Argument, p => p.Name, _ => null);

        if (ally is null)
        {
            ctx.Reply($"You don't see '{ctx.Argument}' here.");
            return;
        }

        // Their chosen target, not the fight they happen to be standing in: a player dragged into
        // combat by an aggressive mob has a CombatState but no CurrentTarget until they swing back,
        // and inheriting the mob's choice would be assisting nobody's decision.
        if (Assisted(ctx, ally) is not { } target)
        {
            ctx.Reply($"{ally.Name} isn't attacking anyone.");
            return;
        }

        if (character.CombatState == CombatState.Fighting && character.CurrentTarget != null)
        {
            ctx.Reply("You're already in combat!");
            return;
        }

        var refusal = target.Player is { } victim
            ? HostileActionGate.RefusePlayer(
                ctx.World, character.RoomKey, character.Id, victim.CharacterId, victim.Name)
            : HostileActionGate.RefuseMob(
                ctx.World, ctx.MobTemplates, character.RoomKey, target.Mob!);

        if (refusal is not null)
        {
            ctx.Reply(refusal, "bad");
            return;
        }

        if (target.Player is { } them)
        {
            EngagePlayer(ctx, them);
        }
        else
        {
            EngageMob(ctx, target.Mob!);
        }
    }

    /// <summary>
    /// What <paramref name="ally"/> is attacking, if it is still here to be attacked.
    /// </summary>
    /// <remarks>
    /// A target in another room, or one that has since died, reads to the player as "they are not
    /// attacking anyone" — which is true from where they are standing, and better than explaining
    /// that a stale entity id is pointing at nothing.
    /// </remarks>
    private static (PlayerActor? Player, Mob? Mob)? Assisted(CommandContext ctx, PlayerActor ally)
    {
        if (ally.Character.CurrentTarget is not { } targetId)
        {
            return null;
        }

        var here = ctx.Actor.Character.RoomKey;

        if (EntityId.IsMob(targetId))
        {
            return ctx.World.FindMob(EntityId.ToGuid(targetId)) is { } mob &&
                   string.Equals(mob.RoomKey, here.ToString(), StringComparison.Ordinal)
                ? (null, mob)
                : null;
        }

        if (EntityId.IsCharacter(targetId))
        {
            return ctx.World.FindByCharacter(EntityId.ToGuid(targetId)) is { } victim &&
                   victim.RoomKey == here
                ? (victim, null)
                : null;
        }

        return null;
    }

    private static void EngageMob(CommandContext ctx, Mob mob)
    {
        var displayName = MobLabel.For(ctx.World, mob);

        // The six steps of starting a fight live in one place, because three things now do it
        // - this verb, a taunt, and a damaging ability landing on something not yet engaged.
        CombatEngagement.Engage(ctx.World, ctx.Actor.Character, mob);

        ctx.Reply($"You begin attacking {NarrationHelper.WithArticle(displayName)}!");
        ctx.Broadcast($"{ctx.Actor.Name} attacks {NarrationHelper.WithArticle(displayName)}!");
    }

    private static void EngagePlayer(CommandContext ctx, PlayerActor victim)
    {
        var character = ctx.Actor.Character;
        var combat = ctx.World.GetOrCreateCombat(character.RoomKey);
        var targetId = EntityId.ForCharacter(victim.CharacterId);

        character.CombatState = CombatState.Fighting;
        character.CurrentTarget = targetId;
        combat.AddCombatant(EntityId.ForCharacter(character.Id));
        combat.AddCombatant(targetId);
        combat.PlayerTargets[character.Id] = targetId;

        ctx.Reply($"You begin attacking {victim.Name}!");
        ctx.Broadcast($"{ctx.Actor.Name} attacks {victim.Name}!");
        victim.Character.CombatState = CombatState.Fighting;
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
        var targetActor = NameMatch.Best(
            ctx.World.OthersIn(actor.Character.RoomKey, actor), targetName, p => p.Name, _ => null);

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
            var name = MobLabel.For(ctx.World, targetMob);

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
        // Named, not keyed. `RoomKey.ToString()` is "ossara.gatetown.the-gate-yard", which is an
        // authoring identifier and the same class of leak as the innkeeper who answered to
        // `ossara-innkeeper` (BUGS.md #14). The title is in hand either way.
        var here = ctx.World.FindRoom(character.RoomKey)?.Title;
        var hereName = string.IsNullOrEmpty(here) ? character.RoomKey.ToString() : here;

        var previousName = previous is { } old
            ? ctx.World.FindRoom(old)?.Title is { Length: > 0 } title ? title : old.ToString()
            : null;

        ctx.Reply(previousName is null
            ? $"You bind your soul to this place: {hereName}."
            : $"You bind your soul to this place: {hereName} (unbound from {previousName}).");

        ctx.Broadcast($"{ctx.Actor.Name} binds their soul here.");
    }
}
