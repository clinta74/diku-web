using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Tests.Abilities;

public sealed class AbilityValidatorTests
{
    [Fact]
    public void CanCast_WithSufficientCost_Allows()
    {
        // Arrange
        var character = new Character
        {
            Id = Guid.CreateVersion7(),
            AccountId = Guid.CreateVersion7(),
            Name = "TestChar",
            Path = CharacterPath.Warden,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Warden),
            RoomKey = RoomKey.Parse("test.zone.room"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var target = new Character
        {
            Id = Guid.CreateVersion7(),
            AccountId = Guid.CreateVersion7(),
            Name = "Enemy",
            Path = CharacterPath.Shade,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Shade),
            RoomKey = RoomKey.Parse("test.zone.room"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var ability = new Ability
        {
            Key = "test.slash",
            Name = "Slash",
            Description = "A basic slash",
            CostType = CostType.Stamina,
            CostValue = 10,
            CooldownPulses = 0,
            CastTimePulses = null,
            TargetingType = TargetingType.SingleTarget,
            EffectKey = "damage.physical",
        };

        // Act (in a PvP room to allow player-vs-player)
        var result = AbilityValidator.CanCast(character, ability, target, false, true);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void CanCast_WithInsufficientCost_Refuses()
    {
        // Arrange
        var character = new Character
        {
            Id = Guid.CreateVersion7(),
            AccountId = Guid.CreateVersion7(),
            Name = "TestChar",
            Path = CharacterPath.Adept,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Adept),
            RoomKey = RoomKey.Parse("test.zone.room"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        character.Vitals.Focus = 5; // Not enough

        var ability = new Ability
        {
            Key = "test.bolt",
            Name = "Bolt",
            Description = "A magical bolt",
            CostType = CostType.Focus,
            CostValue = 20,
            CooldownPulses = 0,
            CastTimePulses = null,
            TargetingType = TargetingType.SingleTarget,
            EffectKey = "damage.magical",
        };

        // Act
        var result = AbilityValidator.CanCast(character, ability, null, false, false);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("Not enough", result.RefusalReason);
    }

    [Fact]
    public void CanCast_SelfTargeted_Allows()
    {
        // Arrange
        var character = new Character
        {
            Id = Guid.CreateVersion7(),
            AccountId = Guid.CreateVersion7(),
            Name = "Healer",
            Path = CharacterPath.Channeler,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Channeler),
            RoomKey = RoomKey.Parse("test.zone.room"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var ability = new Ability
        {
            Key = "test.mend",
            Name = "Mend",
            Description = "Heal yourself",
            CostType = CostType.Focus,
            CostValue = 15,
            CooldownPulses = 0,
            CastTimePulses = null,
            TargetingType = TargetingType.Self,
            EffectKey = "heal.restore",
        };

        // Act
        var result = AbilityValidator.CanCast(character, ability, character, false, false);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void CanCast_SelfTargeted_WithDifferentTarget_Refuses()
    {
        // Arrange
        var caster = new Character
        {
            Id = Guid.CreateVersion7(),
            AccountId = Guid.CreateVersion7(),
            Name = "Caster",
            Path = CharacterPath.Channeler,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Channeler),
            RoomKey = RoomKey.Parse("test.zone.room"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var other = new Character
        {
            Id = Guid.CreateVersion7(),
            AccountId = Guid.CreateVersion7(),
            Name = "Other",
            Path = CharacterPath.Warden,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Warden),
            RoomKey = RoomKey.Parse("test.zone.room"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var ability = new Ability
        {
            Key = "test.mend",
            Name = "Mend",
            Description = "Heal yourself",
            CostType = CostType.Focus,
            CostValue = 15,
            CooldownPulses = 0,
            CastTimePulses = null,
            TargetingType = TargetingType.Self,
            EffectKey = "heal.restore",
        };

        // Act
        var result = AbilityValidator.CanCast(caster, ability, other, false, false);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("only targets yourself", result.RefusalReason);
    }
}
