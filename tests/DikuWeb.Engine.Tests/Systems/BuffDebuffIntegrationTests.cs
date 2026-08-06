using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Integration tests for the buff/debuff system: effect application, stacking rules,
/// expiry on the 60s tick, and interaction with the ability system.
/// </summary>
public sealed class BuffDebuffIntegrationTests
{
    private static readonly RoomKey TestRoom = RoomKey.Parse("test.zone.west");

    /// <summary>Mock repository for testing ability lookups without a database.</summary>
    private sealed class MockAbilityRepository : IAbilityRepository
    {
        private readonly List<Ability> _abilities = [];

        public void Add(Ability ability) => _abilities.Add(ability);

        public Task<IReadOnlyList<Ability>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<Ability>)_abilities.AsReadOnly());

        public Task<Ability?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(_abilities.FirstOrDefault(a => a.Key == key));
    }

    private sealed class TestHarness
    {
        private readonly WorldHarness _world;
        private readonly SystemGameClock _clock;
        private readonly EffectRegistry _effects;
        private readonly AbilityCache _abilityCache;
        private readonly AbilitySystem _abilitySystem;
        private readonly MockAbilityRepository _abilityRepo;

        public TestHarness()
        {
            _world = new WorldHarness();
            _world.LoadTestWorld();

            _clock = new SystemGameClock(TimeProvider.System);
            _effects = new EffectRegistry();
            _abilityCache = new AbilityCache();
            _abilityRepo = new MockAbilityRepository();
            _abilitySystem = new AbilitySystem(_clock, _abilityCache, _effects);
        }

        public WorldHarness World => _world;
        public SystemGameClock Clock => _clock;
        public EffectRegistry Effects => _effects;
        public AbilitySystem AbilitySystem => _abilitySystem;

        public void LoadAbilitiesForTest(params Ability[] abilities)
        {
            foreach (var ability in abilities)
            {
                _abilityRepo.Add(ability);
            }
        }

        public async Task InitializeAbilityCacheAsync()
        {
            await _abilityCache.LoadAsync(_abilityRepo, CancellationToken.None);
        }

        public void AdvanceTicks(long count)
        {
            for (var i = 0; i < count; i++)
            {
                _clock.Advance();
            }
        }

        public long CurrentPulse => _clock.CurrentPulse;
    }

    // -----------------------------------------------------------------------
    // Effect Application and Stacking
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplySingleBuff_AddsNewActiveEffect()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var effect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = $"c_{character.Id:N}",
            OutgoingDamageMultiplier = 1.5m,
            IncomingDamageMultiplier = 1.0m,
            ExpiresAtPulse = currentPulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, effect);
        var effects = harness.World.World.GetActiveEffects(character.Id);

        Assert.Single(effects);
        Assert.Equal("buff.damage-up", effects[0].EffectKey);
        Assert.Equal(1.5m, effects[0].OutgoingDamageMultiplier);
    }

    [Fact]
    public void ApplyDebuff_AddsActiveEffectToTarget()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var debuff = new ActiveEffect
        {
            EffectKey = "debuff.weaken",
            Name = "weakness",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.0m,
            IncomingDamageMultiplier = 1.3m,
            ExpiresAtPulse = currentPulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, debuff);
        var effects = harness.World.World.GetActiveEffects(character.Id);

        Assert.Single(effects);
        Assert.Equal("debuff.weaken", effects[0].EffectKey);
        Assert.Equal(1.3m, effects[0].IncomingDamageMultiplier);
    }

    [Fact]
    public void StackingRule_Refresh_ResetsExpiryOnly()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;
        var casterEntityId = $"c_{Guid.CreateVersion7():N}";

        // Apply first buff
        var firstBuff = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = casterEntityId,
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 100,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };
        harness.World.World.ApplyEffect(character.Id, firstBuff);

        // Re-apply same effect later
        var secondBuff = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = casterEntityId,
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 200,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };
        harness.World.World.ApplyEffect(character.Id, secondBuff);

        var effects = harness.World.World.GetActiveEffects(character.Id);
        Assert.Single(effects);
        Assert.Equal(currentPulse + 200, effects[0].ExpiresAtPulse);
        Assert.Equal(1, effects[0].Stacks);  // Stacks didn't increase
    }

    [Fact]
    public void StackingRule_Stack_IncrementsStacksUpToMax()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;
        var casterEntityId = $"c_{Guid.CreateVersion7():N}";

        // Apply stackable buff
        var effect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = casterEntityId,
            OutgoingDamageMultiplier = 1.2m,
            ExpiresAtPulse = currentPulse + 240,
            Stacks = 1,
            MaxStacks = 3,
            StackingRule = EffectStackingRule.Stack,
        };

        harness.World.World.ApplyEffect(character.Id, effect);
        harness.World.World.ApplyEffect(character.Id, effect);
        harness.World.World.ApplyEffect(character.Id, effect);
        harness.World.World.ApplyEffect(character.Id, effect);  // Should be capped at 3

        var effects = harness.World.World.GetActiveEffects(character.Id);
        Assert.Single(effects);
        Assert.Equal(3, effects[0].Stacks);  // Capped at MaxStacks
    }

    [Fact]
    public void StackingRule_Ignore_DoesNothing()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;
        var casterEntityId = $"c_{Guid.CreateVersion7():N}";

        // Apply effect with Ignore stacking rule
        var firstEffect = new ActiveEffect
        {
            EffectKey = "debuff.weaken",
            Name = "weakness",
            SourceEntityId = casterEntityId,
            IncomingDamageMultiplier = 1.3m,
            ExpiresAtPulse = currentPulse + 100,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Ignore,
        };
        harness.World.World.ApplyEffect(character.Id, firstEffect);

        // Try to re-apply with later expiry
        var secondEffect = new ActiveEffect
        {
            EffectKey = "debuff.weaken",
            Name = "weakness",
            SourceEntityId = casterEntityId,
            IncomingDamageMultiplier = 1.3m,
            ExpiresAtPulse = currentPulse + 300,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Ignore,
        };
        harness.World.World.ApplyEffect(character.Id, secondEffect);

        var effects = harness.World.World.GetActiveEffects(character.Id);
        Assert.Single(effects);
        Assert.Equal(currentPulse + 100, effects[0].ExpiresAtPulse);  // Not updated
    }

    [Fact]
    public void MultipleEffectsFromDifferentSources_AllApply()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;
        var caster1 = $"c_{Guid.CreateVersion7():N}";
        var caster2 = $"c_{Guid.CreateVersion7():N}";

        var buff1 = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = caster1,
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        var buff2 = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = caster2,
            OutgoingDamageMultiplier = 1.3m,
            ExpiresAtPulse = currentPulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, buff1);
        harness.World.World.ApplyEffect(character.Id, buff2);

        var effects = harness.World.World.GetActiveEffects(character.Id);
        Assert.Equal(2, effects.Count);
        Assert.Contains(effects, e => e.SourceEntityId == caster1);
        Assert.Contains(effects, e => e.SourceEntityId == caster2);
    }

    // -----------------------------------------------------------------------
    // Effect Expiry
    // -----------------------------------------------------------------------

    [Fact]
    public void ExpireEffects_RemovesEffectsAtExpirationPulse()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var effect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = $"c_{character.Id:N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 10,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, effect);
        Assert.Single(harness.World.World.GetActiveEffects(character.Id));

        // Expire at exact pulse
        var expired = harness.World.World.ExpireEffects(currentPulse + 10);
        Assert.Single(expired);
        Assert.Empty(harness.World.World.GetActiveEffects(character.Id));
    }

    [Fact]
    public void ExpireEffects_OnlyRemovesExpiredEffects()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var shortEffect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "short buff",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 10,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        var longEffect = new ActiveEffect
        {
            EffectKey = "debuff.weaken",
            Name = "weakness",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            IncomingDamageMultiplier = 1.3m,
            ExpiresAtPulse = currentPulse + 100,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, shortEffect);
        harness.World.World.ApplyEffect(character.Id, longEffect);

        var expired = harness.World.World.ExpireEffects(currentPulse + 50);
        Assert.Single(expired);
        Assert.Equal("buff.damage-up", expired[0].EffectKey);

        var remaining = harness.World.World.GetActiveEffects(character.Id);
        Assert.Single(remaining);
        Assert.Equal("debuff.weaken", remaining[0].EffectKey);
    }

    [Fact]
    public void ExpireEffects_NoEffectsBefore_ReturnsEmpty()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var effect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = $"c_{character.Id:N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 100,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, effect);
        var expired = harness.World.World.ExpireEffects(currentPulse + 50);

        Assert.Empty(expired);
        Assert.Single(harness.World.World.GetActiveEffects(character.Id));
    }

    [Fact]
    public void ExpireEffects_CleansUpEmptyLists()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var effect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = $"c_{character.Id:N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 10,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, effect);
        harness.World.World.ExpireEffects(currentPulse + 10);

        // After expiry, GetActiveEffects should return empty, not throw
        var remaining = harness.World.World.GetActiveEffects(character.Id);
        Assert.Empty(remaining);
    }

    // -----------------------------------------------------------------------
    // Ability System Integration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CastAbilityWithBuffEffect_CreatesActiveEffect()
    {
        var harness = new TestHarness();
        var caster = harness.World.AddPlayer("Kael", TestRoom).Character;
        var target = harness.World.AddPlayer("Tarn", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var buffAbility = new Ability
        {
            Key = "warden.damage-up",
            Name = "Damage Boost",
            Description = "Increase outgoing damage",
            CostType = CostType.Focus,
            CostValue = 10,
            CooldownPulses = 60,
            TargetingType = TargetingType.SingleTarget,
            EffectKey = "buff.damage-up",
            EffectParams = new()
            {
                { "outgoingMultiplier", "1.5" },
                { "durationPulses", "240" },
                { "maxStacks", "1" },
                { "stackingRule", "Refresh" },
                { "name", "battle fury" },
            },
        };

        harness.LoadAbilitiesForTest(buffAbility);
        await harness.InitializeAbilityCacheAsync();

        // Create a cast job for the ability
        var cast = new CastJob
        {
            CharacterId = caster.Id,
            AbilityKey = "warden.damage-up",
            TargetId = $"c_{target.Id}",
            StartingRoomKey = caster.RoomKey.ToString(),
            ResolveAtPulse = currentPulse,
        };

        harness.World.World.CastQueue.Enqueue(cast);
        harness.AbilitySystem.Tick(harness.World.World);

        // Verify effect was applied to target
        var effects = harness.World.World.GetActiveEffects(target.Id);
        Assert.Single(effects);
        Assert.Equal("buff.damage-up", effects[0].EffectKey);
        Assert.Equal("battle fury", effects[0].Name);
        Assert.Equal(1.5m, effects[0].OutgoingDamageMultiplier);
    }

    [Fact]
    public async Task CastAbilityWithDebuffEffect_CreatesActiveEffectOnTarget()
    {
        var harness = new TestHarness();
        var caster = harness.World.AddPlayer("Adept", TestRoom).Character;
        var target = harness.World.AddPlayer("Mob", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var debuffAbility = new Ability
        {
            Key = "adept.weaken",
            Name = "Weaken",
            Description = "Weaken enemy defense",
            CostType = CostType.Focus,
            CostValue = 15,
            CooldownPulses = 60,
            TargetingType = TargetingType.SingleTarget,
            EffectKey = "debuff.weaken",
            EffectParams = new()
            {
                { "incomingMultiplier", "1.3" },
                { "durationPulses", "240" },
                { "maxStacks", "1" },
                { "stackingRule", "Refresh" },
                { "name", "weakness" },
            },
        };

        harness.LoadAbilitiesForTest(debuffAbility);
        await harness.InitializeAbilityCacheAsync();

        var cast = new CastJob
        {
            CharacterId = caster.Id,
            AbilityKey = "adept.weaken",
            TargetId = $"c_{target.Id}",
            StartingRoomKey = caster.RoomKey.ToString(),
            ResolveAtPulse = currentPulse,
        };

        harness.World.World.CastQueue.Enqueue(cast);
        harness.AbilitySystem.Tick(harness.World.World);

        var effects = harness.World.World.GetActiveEffects(target.Id);
        Assert.Single(effects);
        Assert.Equal("debuff.weaken", effects[0].EffectKey);
        Assert.Equal(1.3m, effects[0].IncomingDamageMultiplier);
    }

    [Fact]
    public async Task SelfTargetBuff_AppliesEffectToCaster()
    {
        var harness = new TestHarness();
        var caster = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var selfBuffAbility = new Ability
        {
            Key = "warden.fortify",
            Name = "Fortify",
            Description = "Increase defense",
            CostType = CostType.Focus,
            CostValue = 10,
            CooldownPulses = 60,
            TargetingType = TargetingType.Self,
            EffectKey = "buff.damage-up",
            EffectParams = new()
            {
                { "incomingMultiplier", "0.8" },
                { "durationPulses", "240" },
                { "maxStacks", "1" },
                { "stackingRule", "Refresh" },
                { "name", "fortified" },
            },
        };

        harness.LoadAbilitiesForTest(selfBuffAbility);
        await harness.InitializeAbilityCacheAsync();

        var cast = new CastJob
        {
            CharacterId = caster.Id,
            AbilityKey = "warden.fortify",
            TargetId = "",
            StartingRoomKey = caster.RoomKey.ToString(),
            ResolveAtPulse = currentPulse,
        };

        harness.World.World.CastQueue.Enqueue(cast);
        harness.AbilitySystem.Tick(harness.World.World);

        var effects = harness.World.World.GetActiveEffects(caster.Id);
        Assert.Single(effects);
        Assert.Equal("buff.damage-up", effects[0].EffectKey);
        Assert.Equal("fortified", effects[0].Name);
    }

    // -----------------------------------------------------------------------
    // Effect Exiry System Integration
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectExpirySystem_ExpiresEffectsOnGameTick()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;
        var currentPulse = harness.CurrentPulse;

        var effect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = $"c_{character.Id:N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 10,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, effect);

        // Advance to expiry pulse
        harness.AdvanceTicks(10);

        // Simulate what EffectExpirySystem.Tick would do
        var expired = harness.World.World.ExpireEffects(harness.CurrentPulse);
        Assert.Single(expired);
        Assert.Empty(harness.World.World.GetActiveEffects(character.Id));
    }

    [Fact]
    public void NoActiveEffects_ReturnsEmptyList()
    {
        var harness = new TestHarness();
        var character = harness.World.AddPlayer("Kael", TestRoom).Character;

        var effects = harness.World.World.GetActiveEffects(character.Id);
        Assert.Empty(effects);
    }

    [Fact]
    public void RemoveCharacter_EffectsAreNotAutomaticallyExpired()
    {
        var harness = new TestHarness();
        var actor = harness.World.AddPlayer("Kael", TestRoom);
        var character = actor.Character;
        var currentPulse = harness.CurrentPulse;

        var effect = new ActiveEffect
        {
            EffectKey = "buff.damage-up",
            Name = "damage boost",
            SourceEntityId = $"c_{Guid.CreateVersion7():N}",
            OutgoingDamageMultiplier = 1.5m,
            ExpiresAtPulse = currentPulse + 240,
            Stacks = 1,
            MaxStacks = 1,
            StackingRule = EffectStackingRule.Refresh,
        };

        harness.World.World.ApplyEffect(character.Id, effect);

        // Remove character from world
        harness.World.World.Remove(actor);

        // Effects remain in the dictionary until explicitly expired
        var effects = harness.World.World.GetActiveEffects(character.Id);
        Assert.Single(effects);
    }
}
