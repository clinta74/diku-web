using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Entities;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Time;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Ability-related commands: cast and list known abilities.
/// </summary>
public static class AbilityCommands
{
    public static void Register(
        List<CommandDefinition> commands,
        AbilityCache? abilityCache = null,
        IGameClock? clock = null,
        EffectRegistry? effects = null)
    {
        // Defaulted rather than left null, because this table is only ever read to ask which way
        // an ability points - and a null one would answer "helpful" for everything, quietly
        // turning a bare `cast scorch` into setting yourself on fire. The registry is a lookup
        // over seven built-ins with no state, so a spare instance costs nothing.
        var effectTable = effects ?? new EffectRegistry();

        commands.Add(new CommandDefinition(
            "cast", 1, "cast <ability> [target] (c) - cast an ability",
            ctx => Cast(ctx, abilityCache, clock, effectTable)));

        commands.Add(new CommandDefinition(
            "abilities", 0, "abilities (ab) - list your known abilities",
            ctx => ListAbilities(ctx, abilityCache)));
    }

    private static void Cast(
        CommandContext ctx,
        AbilityCache? cache,
        IGameClock? clock,
        EffectRegistry effects)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Cast what ability?");
            return;
        }

        var character = ctx.Actor.Character;

        // Stunned means stunned: no swings in the combat loop, and no new casts here either.
        // Gating only the loop would leave a stunned caster free to keep casting, which is the
        // half of "cannot act" that matters most against an Adept.
        if (ctx.World.IsStunned(character.Id, clock?.CurrentPulse ?? 0L))
        {
            ctx.Reply("You cannot gather yourself.", "bad");
            return;
        }

        // Posture, on the same footing as the stun above and for the same reason: an ability is an
        // action, and rest is the state of not taking any. Refused before the ability is even
        // resolved, so a sleeping character is told they are asleep rather than told they do not
        // know a spell they know perfectly well.
        if (RestGate.Refuse(character) is { } resting)
        {
            ctx.Reply(resting, "bad");
            return;
        }

        // Determine which abilities the character knows (at their level). Read from the loaded
        // table, so a retune or a newly authored ability takes effect without a restart.
        var knownAbilityKeys = AbilityProgression.GetKnownAbilitiesForLevel(
            cache?.All.Values ?? [], character.Path, character.Level);
        if (knownAbilityKeys.Count == 0)
        {
            ctx.Reply("You don't know any abilities yet.");
            return;
        }

        // The ability and the target are worked out together, because where one ends and the
        // other begins is not decidable a word at a time - "shield bash rat" has to know that
        // Shield Bash is a thing before it can tell that "rat" is the target.
        var match = AbilityLookup.Resolve(cache, character, ctx.Argument);

        if (!match.Found)
        {
            ctx.Reply($"You don't know an ability called '{ctx.Argument}'.");
            return;
        }

        var ability = match.Ability!;
        var targetName = match.Target;
        var matchingKey = ability.Key;
        var kind = AbilityKinds.Of(ability);

        // `cast kick` reads wrong because it is wrong: a boot to the knee is not a spell. The
        // split is not invented for this - the catalogue already draws it, since the two caster
        // Paths pay Focus for all eighteen of their abilities and the two martial Paths pay
        // Stamina.
        //
        // Refused rather than quietly allowed, because the point is to teach the vocabulary, and
        // the verb form always works. Any prefix of "cast" reaches the verb, since it takes a
        // single character - so this is asking "did they type cast", not "did they type c".
        if (kind == AbilityKind.Skill &&
            "cast".StartsWith(ctx.Verb, StringComparison.OrdinalIgnoreCase))
        {
            var usage = targetName is null
                ? ability.Name.ToLowerInvariant()
                : $"{ability.Name.ToLowerInvariant()} {targetName}";

            ctx.Reply($"{ability.Name} is a skill, not a spell. Just type '{usage}'.", "bad");
            return;
        }

        // Check cooldown. An ability this character has never cast has no last-cast pulse at
        // all, which is not the same as having cast it on pulse 0.
        var currentPulse = clock?.CurrentPulse ?? 0L;
        if (ctx.World.GetAbilityCooldown(character.Id, matchingKey) is { } lastCastPulse)
        {
            var cooldownRemaining = (lastCastPulse + ability.CooldownPulses) - currentPulse;
            if (cooldownRemaining > 0)
            {
                var secondsRemaining = Math.Ceiling(cooldownRemaining * 0.25); // 250ms per pulse
                ctx.Reply($"{ability.Name} is on cooldown ({secondsRemaining}s remaining).");
                return;
            }
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
            ctx.Reply($"Not enough {ability.CostType.ToString().ToLowerInvariant()} (need {ability.CostValue}, have {currentResource}).");
            return;
        }

        var targetId = ResolveTarget(ctx, ability, targetName, effects);

        // A single-target ability with nothing to aim at must not be paid for. Cost and cooldown
        // are spent by the ability system before it resolves a target, so casting at a name that
        // matched nothing used to charge in full, start the cooldown, and narrate "takes effect!"
        // over the top of an effect that never ran.
        if (ability.TargetingType == TargetingType.SingleTarget && targetId is null)
        {
            ctx.Reply(
                targetName is null
                    ? $"{ability.Name} needs a target."
                    : $"You don't see '{targetName}' here.",
                "bad");
            return;
        }

        // A hostile ability answers to §4.11 exactly as a swing does. `kill` has always refused a
        // peaceful room, a non-combatant, and an unsanctioned duel; `cast` checked none of the
        // three, so a Bolt worked in a safe room and an Adept could kill the shopkeeper who hands
        // out the zone's quests. Refused here rather than at resolution, so nothing is spent.
        if (effects.IsHarmful(ability) &&
            HostileRefusal(ctx, targetId) is { } refusal)
        {
            ctx.Reply(refusal, "bad");
            return;
        }

        // Enqueue cast
        var castJob = new CastJob
        {
            CharacterId = character.Id,
            AbilityKey = matchingKey,
            TargetId = targetId,
            // A cast time finally means something. While this is pending the caster's weapons
            // are silent, and a blow that lands will break it.
            ResolveAtPulse = currentPulse + (ability.CastTimePulses ?? 0),
            StartingRoomKey = character.RoomKey.ToString(),
        };

        ctx.World.CastQueue.Enqueue(castJob);

        // A Warden does not cast a kick. The verb follows the kind, so the prose matches what the
        // character is actually doing.
        var first = kind == AbilityKind.Spell ? "cast" : "use";
        var third = kind == AbilityKind.Spell ? "casts" : "uses";

        ctx.Reply($"You {first} {ability.Name}!");

        // Narrate to room
        if (targetId != null)
        {
            var target = ctx.World.OthersIn(character.RoomKey, ctx.Actor)
                .FirstOrDefault(p => EntityId.ForCharacter(p.CharacterId) == targetId);
            if (target != null)
            {
                ctx.Broadcast($"{ctx.Actor.Name} {third} {ability.Name} on {target.Name}!");
                target.SendText($"{ctx.Actor.Name} {third} {ability.Name} on you!", "ability");
            }
            else
            {
                ctx.Broadcast($"{ctx.Actor.Name} {third} {ability.Name}!");
            }
        }
        else
        {
            ctx.Broadcast($"{ctx.Actor.Name} {third} {ability.Name}!");
        }
    }

    /// <summary>
    /// Why this cast may not be aimed where it is aimed, or null if it may be.
    /// </summary>
    /// <remarks>
    /// Self-targeted abilities and area effects both arrive here with a null target id and are
    /// waved through: nothing hostile is aimed at the caster, and an area effect gathers its own
    /// targets at resolution, filtering each one through the same gate.
    /// </remarks>
    private static string? HostileRefusal(CommandContext ctx, string? targetId)
    {
        if (targetId is null || !EntityId.IsWellFormed(targetId))
        {
            return null;
        }

        var roomKey = ctx.Actor.Character.RoomKey;

        if (EntityId.IsMob(targetId))
        {
            return ctx.World.GetMob(EntityId.ToGuid(targetId)) is { } mob
                ? HostileActionGate.RefuseMob(ctx.World, ctx.MobTemplates, roomKey, mob)
                : null;
        }

        var target = ctx.World.GetCharacter(EntityId.ToGuid(targetId));

        // Never yourself, whatever the room says: a harmful ability aimed at the caster is the
        // caster's own business and no business of the pvp flag.
        return target is null || target.Id == ctx.Actor.CharacterId
            ? null
            : HostileActionGate.RefusePlayer(
                ctx.World, roomKey, ctx.Actor.CharacterId, target.Id, target.Name);
    }

    /// <summary>
    /// What this cast is aimed at, as an entity id, or null when it needs no target or found none.
    /// </summary>
    /// <remarks>
    /// This searched players only. A mob was never a candidate, so every offensive ability in the
    /// game resolved to no target against the things you actually fight - and because cost and
    /// cooldown are spent before the target is resolved, it charged full price and said it worked.
    ///
    /// Falling back to the current combat target is the other half: in a real fight you are
    /// already swinging at something, and retyping its name on every cast is friction with no
    /// purpose.
    /// </remarks>
    private static string? ResolveTarget(
        CommandContext ctx,
        Ability ability,
        string? targetName,
        EffectRegistry effects)
    {
        // Neither of these aims at one thing. An area effect gathers its own targets at the
        // moment it lands, so a name typed after it is ignored rather than narrowing it.
        if (ability.TargetingType is TargetingType.Self or TargetingType.Aoe)
        {
            return null;
        }

        var actor = ctx.Actor;
        var roomKey = actor.Character.RoomKey;

        if (!string.IsNullOrEmpty(targetName))
        {
            // Yourself first. The player search below is OthersIn, which excludes the caster, so
            // a Hallow typing their own name at their own heal - the obvious thing to type - got
            // "You don't see 'Bram' here." while standing there.
            if (string.Equals(actor.Name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return EntityId.ForCharacter(actor.CharacterId);
            }

            var player = ctx.World.OthersIn(roomKey, actor)
                .FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));

            if (player is not null)
            {
                return EntityId.ForCharacter(player.CharacterId);
            }

            // Matched the same way every other targeting command matches, so "bolt giant" reaches
            // the giant rat and does not silently mean "no target".
            var mob = NameMatch.Best(
                ctx.World.MobsIn(roomKey), targetName, m => m.TemplateName, m => m.TemplateKey);

            return mob is null ? null : EntityId.ForMob(mob.Id);
        }

        // No name given. What that should mean depends on which way the ability points: a bolt
        // means "the thing I am fighting", a heal means "me". Falling back to the combat target
        // for both would have a Hallow mending the wolf that is biting them.
        return effects.IsHarmful(ability)
            ? actor.Character.CurrentTarget
            : EntityId.ForCharacter(actor.CharacterId);
    }

    private static void ListAbilities(CommandContext ctx, AbilityCache? cache)
    {
        var character = ctx.Actor.Character;
        var knownAbilities = AbilityProgression.GetKnownAbilitiesForLevel(
            cache?.All.Values ?? [], character.Path, character.Level);
        var knownPassives = AbilityProgression.GetKnownPassivesForLevel(character.Path, character.Level);

        if (knownAbilities.Count == 0 && knownPassives.Count == 0)
        {
            ctx.Reply("You don't know any abilities yet.");
            return;
        }

        // Grouped by kind and shown by name, because the name is what you type. This used to
        // print raw keys - "warden.shield-bash" - which taught players an implementation detail
        // and, worse, taught it as though it were the thing to type.
        var resolved = knownAbilities
            .Select(key => (Key: key, Ability: cache?.Get(key)))
            .ToList();

        foreach (var kind in new[] { AbilityKind.Skill, AbilityKind.Spell })
        {
            var group = resolved
                .Where(r => r.Ability is not null && AbilityKinds.Of(r.Ability) == kind)
                .ToList();

            if (group.Count == 0)
            {
                continue;
            }

            ctx.Reply(
                kind == AbilityKind.Spell
                    ? $"Your spells ({character.Path}) — 'cast <name> [target]':"
                    : $"Your skills ({character.Path}) — type the name: '<name> [target]':",
                "heading");

            foreach (var (_, ability) in group)
            {
                ctx.Reply(
                    $"  • {ability!.Name}  ({ability.CostValue} {ability.CostType.ToString().ToLowerInvariant()})");
            }
        }

        // Anything the cache could not resolve is still worth naming: a key with no row behind it
        // is a content problem, and silently omitting it would hide it from the person best
        // placed to report it.
        foreach (var (key, ability) in resolved.Where(r => r.Ability is null))
        {
            ctx.Reply($"  • {key} — not configured", "bad");
        }

        // Passives are never cast, so they are listed apart from the things `cast` accepts -
        // running them together would read as a spell that refuses to work.
        if (knownPassives.Count > 0)
        {
            ctx.Reply("Passives:");
            foreach (var key in knownPassives)
            {
                ctx.Reply($"  • {PassiveKeys.NameOf(key)} — {PassiveKeys.DescriptionOf(key)}");
            }
        }
    }
}
