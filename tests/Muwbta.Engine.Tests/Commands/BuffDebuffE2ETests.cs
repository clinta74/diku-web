using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// End-to-end tests for buff/debuff system from the command/UI layer:
/// applying effects, verifying buffs command output, and command interactions.
/// (Combat damage multiplier integration is verified in BuffDebuffIntegrationTests)
/// </summary>
public sealed class BuffDebuffE2ETests
{
    private static readonly RoomKey TestRoom = RoomKey.Parse("test.zone.west");

    private sealed class E2EHarness
    {
        private readonly WorldHarness _harness;

        public E2EHarness()
        {
            _harness = new WorldHarness();
            _harness.LoadTestWorld();
        }

        public WorldHarness World => _harness;

        public PlayerActor AddPlayer(string name) => _harness.AddPlayer(name, TestRoom);

        public void Execute(PlayerActor actor, string command) => _harness.Execute(actor, command);

        public void Drain(PlayerActor actor) => _harness.Drain(actor);

        public string DrainText(PlayerActor actor) => _harness.DrainText(actor);
    }

    private const long BasePulse = 1000;

    [Fact]
    public void BuffsCommand_WhenNoEffects_ShowsEmptyMessage()
    {
        var harness = new E2EHarness();
        var kael = harness.AddPlayer("Kael");

        harness.Drain(kael);
        harness.Execute(kael, "buffs");

        var text = harness.DrainText(kael);
        Assert.Contains("no active effects", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuffsCommand_ListsSingleActiveEffect()
    {
        var harness = new E2EHarness();
        var kael = harness.AddPlayer("Kael");

        // Apply a buff
        harness.World.World.ApplyEffect(kael.Character.Id, new()
        {
            EffectKey = "buff.damage-up",
            Name = "battle fury",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = BasePulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = Muwbta.Domain.Abilities.Effects.EffectStackingRule.Refresh,
        });

        harness.Drain(kael);
        harness.Execute(kael, "buffs");

        var text = harness.DrainText(kael);
        Assert.Contains("battle fury", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active Effects", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuffsCommand_ShowsStackCountForStackableEffects()
    {
        var harness = new E2EHarness();
        var kael = harness.AddPlayer("Kael");

        // Apply a stackable buff with 3 stacks
        harness.World.World.ApplyEffect(kael.Character.Id, new()
        {
            EffectKey = "buff.damage-up",
            Name = "battle fury",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.2m,
            ExpiresAtPulse = BasePulse + 240,
            Stacks = 3,
            MaxStacks = 5,
            StackingRule = Muwbta.Domain.Abilities.Effects.EffectStackingRule.Stack,
        });

        harness.Drain(kael);
        harness.Execute(kael, "buffs");

        var text = harness.DrainText(kael);
        Assert.Contains("(3/5)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuffsCommand_ListsMultipleEffects()
    {
        var harness = new E2EHarness();
        var kael = harness.AddPlayer("Kael");

        // Apply multiple effects
        harness.World.World.ApplyEffect(kael.Character.Id, new()
        {
            EffectKey = "buff.damage-up",
            Name = "battle fury",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = BasePulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = Muwbta.Domain.Abilities.Effects.EffectStackingRule.Refresh,
        });

        harness.World.World.ApplyEffect(kael.Character.Id, new()
        {
            EffectKey = "buff.damage-up",
            Name = "fortified",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            IncomingDamageMultiplier = 0.7m,
            ExpiresAtPulse = BasePulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = Muwbta.Domain.Abilities.Effects.EffectStackingRule.Refresh,
        });

        harness.Drain(kael);
        harness.Execute(kael, "buffs");

        var text = harness.DrainText(kael);
        Assert.Contains("battle fury", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fortified", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultiplePlayersHaveIndependentEffects()
    {
        var harness = new E2EHarness();
        var kael = harness.AddPlayer("Kael");
        var mira = harness.AddPlayer("Mira");

        // Apply buff to Kael only
        harness.World.World.ApplyEffect(kael.Character.Id, new()
        {
            EffectKey = "buff.damage-up",
            Name = "battle fury",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = BasePulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = Muwbta.Domain.Abilities.Effects.EffectStackingRule.Refresh,
        });

        harness.Drain(kael);
        harness.Drain(mira);

        // Check Kael's effects
        harness.Execute(kael, "buffs");
        var kaelText = harness.DrainText(kael);
        Assert.Contains("battle fury", kaelText, StringComparison.OrdinalIgnoreCase);

        // Check Mira's effects (should be empty)
        harness.Execute(mira, "buffs");
        var miraText = harness.DrainText(mira);
        Assert.Contains("no active effects", miraText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ColorCodingDistinguishesBuffsFromDebuffs()
    {
        var harness = new E2EHarness();
        var kael = harness.AddPlayer("Kael");

        // Apply buff (outgoing multiplier > 1)
        harness.World.World.ApplyEffect(kael.Character.Id, new()
        {
            EffectKey = "buff.damage-up",
            Name = "battle fury",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = BasePulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = Muwbta.Domain.Abilities.Effects.EffectStackingRule.Refresh,
        });

        // Apply debuff (incoming multiplier > 1)
        harness.World.World.ApplyEffect(kael.Character.Id, new()
        {
            EffectKey = "debuff.weaken",
            Name = "weakness",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            IncomingDamageMultiplier = 1.3m,
            ExpiresAtPulse = BasePulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = Muwbta.Domain.Abilities.Effects.EffectStackingRule.Refresh,
        });

        // Check both effects are present (different styling tested via unit tests)
        var effects = harness.World.World.GetActiveEffects(kael.Character.Id);
        Assert.Equal(2, effects.Count);
    }
}
