using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;

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

        commands.Add(new CommandDefinition(
            "bind", 0, "bind (b) - set respawn point in this room", Bind));
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

        // Validate target before entering combat
        var peaceful = ctx.World.IsFlagSet(character.RoomKey, RoomFlags.Peaceful);
        var pvp = ctx.World.IsFlagSet(character.RoomKey, RoomFlags.Pvp);

        if (targetActor != null)
        {
            var validation = TargetValidator.ValidateTarget(
                CombatantType.Player, CombatantType.Player, targetActor.Name, peaceful, pvp);
            if (!validation.IsAllowed)
            {
                ctx.Reply(validation.RefusalReason ?? "You cannot attack that target.");
                return;
            }
        }
        else if (targetMob != null)
        {
            var validation = TargetValidator.ValidateTarget(
                CombatantType.Player, CombatantType.Mob, targetMob.TemplateKey, peaceful, pvp);
            if (!validation.IsAllowed)
            {
                ctx.Reply(validation.RefusalReason ?? "You cannot attack that target.");
                return;
            }
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
            var targetId = EntityId.ForMob(targetMob.Id);
            character.CombatState = CombatState.Fighting;
            character.CurrentTarget = targetId;
            combat.AddCombatant(EntityId.ForCharacter(character.Id));
            combat.AddCombatant(targetId);
            combat.PlayerTargets[character.Id] = targetId;

            // Seed hate so the mob fights back from the moment it is attacked rather than from
            // the moment it is first hurt. It picks its target by top hater, and with weapons
            // now swinging on their own clocks a slow opener would otherwise leave it standing
            // there. This mirrors what MobAiSystem does when a mob starts the fight itself.
            combat.AddToHateList(targetId, EntityId.ForCharacter(character.Id), 1);

            ctx.Reply($"You begin attacking {NarrationHelper.WithArticle(displayName)}!");
            ctx.Broadcast($"{actor.Name} attacks {NarrationHelper.WithArticle(displayName)}!");
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

        // Check if room allows binding
        if (!ctx.World.IsFlagSet(character.RoomKey, RoomFlags.Respawn))
        {
            ctx.Reply("You cannot bind your soul in this place.");
            return;
        }

        character.RespawnRoomKey = character.RoomKey;
        ctx.Reply($"You bind your soul to this place: {character.RoomKey}");
        ctx.Broadcast($"{ctx.Actor.Name} binds their soul here.");
    }
}
