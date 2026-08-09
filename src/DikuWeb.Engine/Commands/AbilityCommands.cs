using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Entities;
using DikuWeb.Engine.Abilities;
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
            "abilities", 0, "abilities (ab) - list your known abilities", ListAbilities));
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

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var abilityNameOrKey = parts[0];
        var targetName = parts.Length > 1 ? parts[1] : null;

        var character = ctx.Actor.Character;

        // Stunned means stunned: no swings in the combat loop, and no new casts here either.
        // Gating only the loop would leave a stunned caster free to keep casting, which is the
        // half of "cannot act" that matters most against an Adept.
        if (ctx.World.IsStunned(character.Id, clock?.CurrentPulse ?? 0L))
        {
            ctx.Reply("You cannot gather yourself.", "bad");
            return;
        }

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

        // Narrate to player
        ctx.Reply($"You cast {ability.Name}!");

        // Narrate to room
        if (targetId != null)
        {
            var target = ctx.World.OthersIn(character.RoomKey, ctx.Actor)
                .FirstOrDefault(p => EntityId.ForCharacter(p.CharacterId) == targetId);
            if (target != null)
            {
                ctx.Broadcast($"{ctx.Actor.Name} casts {ability.Name} on {target.Name}!");
                target.SendText($"{ctx.Actor.Name} casts {ability.Name} on you!", "ability");
            }
            else
            {
                ctx.Broadcast($"{ctx.Actor.Name} casts {ability.Name}!");
            }
        }
        else
        {
            ctx.Broadcast($"{ctx.Actor.Name} casts {ability.Name}!");
        }
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
        return effects.Get(ability.EffectKey)?.IsHarmful == true
            ? actor.Character.CurrentTarget
            : EntityId.ForCharacter(actor.CharacterId);
    }

    private static void ListAbilities(CommandContext ctx)
    {
        var character = ctx.Actor.Character;
        var knownAbilities = AbilityProgression.GetKnownAbilitiesForLevel(character.Path, character.Level);
        var knownPassives = AbilityProgression.GetKnownPassivesForLevel(character.Path, character.Level);

        if (knownAbilities.Count == 0 && knownPassives.Count == 0)
        {
            ctx.Reply("You don't know any abilities yet.");
            return;
        }

        if (knownAbilities.Count > 0)
        {
            ctx.Reply($"Your abilities ({character.Path}):");
            foreach (var key in knownAbilities)
            {
                ctx.Reply($"  • {key}");
            }
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
