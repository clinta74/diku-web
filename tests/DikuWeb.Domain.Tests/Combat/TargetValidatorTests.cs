using DikuWeb.Domain.Combat;

namespace DikuWeb.Domain.Tests.Combat;

public sealed class TargetValidatorTests
{
    [Fact]
    public void Peaceful_room_forbids_all_combat()
    {
        // Player attacking player
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Player,
            "Kael",
            roomIsPeaceful: true,
            roomIsPvp: false);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RefusalReason);
        Assert.Contains("Kael", result.RefusalReason);
    }

    [Fact]
    public void Peaceful_room_forbids_player_vs_mob()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Mob,
            "goblin",
            roomIsPeaceful: true,
            roomIsPvp: false);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RefusalReason);
        Assert.Contains("goblin", result.RefusalReason);
    }

    [Fact]
    public void Peaceful_room_forbids_mob_vs_player()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Mob,
            CombatantType.Player,
            "Theron",
            roomIsPeaceful: true,
            roomIsPvp: false);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RefusalReason);
        Assert.Contains("Theron", result.RefusalReason);
    }

    [Fact]
    public void Peaceful_room_forbids_mob_vs_mob()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Mob,
            CombatantType.Mob,
            "goblin-sentry",
            roomIsPeaceful: true,
            roomIsPvp: false);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Player_attacking_player_without_pvp_flag_is_forbidden()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Player,
            "Elara",
            roomIsPeaceful: false,
            roomIsPvp: false);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RefusalReason);
        Assert.Contains("Elara", result.RefusalReason);
    }

    [Fact]
    public void Player_attacking_player_with_pvp_flag_is_allowed()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Player,
            "Elara",
            roomIsPeaceful: false,
            roomIsPvp: true);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RefusalReason);
    }

    [Fact]
    public void Player_attacking_mob_in_normal_room_is_allowed()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Mob,
            "goblin",
            roomIsPeaceful: false,
            roomIsPvp: false);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RefusalReason);
    }

    [Fact]
    public void Player_attacking_mob_in_pvp_room_is_allowed()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Mob,
            "goblin",
            roomIsPeaceful: false,
            roomIsPvp: true);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RefusalReason);
    }

    [Fact]
    public void Mob_attacking_player_in_normal_room_is_allowed()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Mob,
            CombatantType.Player,
            "Kael",
            roomIsPeaceful: false,
            roomIsPvp: false);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RefusalReason);
    }

    [Fact]
    public void Mob_attacking_player_without_pvp_is_allowed()
    {
        // Mobs ignore the pvp flag - they can attack players anywhere (except peaceful).
        var result = TargetValidator.ValidateTarget(
            CombatantType.Mob,
            CombatantType.Player,
            "Elara",
            roomIsPeaceful: false,
            roomIsPvp: false);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RefusalReason);
    }

    [Fact]
    public void Mob_attacking_mob_in_normal_room_is_allowed()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Mob,
            CombatantType.Mob,
            "other-goblin",
            roomIsPeaceful: false,
            roomIsPvp: false);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RefusalReason);
    }

    [Fact]
    public void Refusal_reason_contains_target_name()
    {
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Player,
            "Sir Lancelot the Brave",
            roomIsPeaceful: false,
            roomIsPvp: false);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RefusalReason);
        Assert.Contains("Sir Lancelot the Brave", result.RefusalReason);
    }

    [Fact]
    public void Peaceful_overrides_pvp()
    {
        // Even though the room is marked pvp, peaceful takes precedence.
        var result = TargetValidator.ValidateTarget(
            CombatantType.Player,
            CombatantType.Player,
            "Mara",
            roomIsPeaceful: true,
            roomIsPvp: true);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Validates_target_name_is_not_empty()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TargetValidator.ValidateTarget(
                CombatantType.Player,
                CombatantType.Player,
                string.Empty,
                roomIsPeaceful: false,
                roomIsPvp: false));

        Assert.Contains("targetName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
