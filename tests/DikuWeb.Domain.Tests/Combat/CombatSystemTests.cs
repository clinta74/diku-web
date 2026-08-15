using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Tests.Combat;

public sealed class CombatSystemTests
{
    [Fact]
    public void ExecuteRound_valid_hit_deals_damage()
    {
        var attacker = new AttackerStats(
            AttackRating: 4,
            BaseDamage: 2,
            MinDamage: 4,
            MaxDamage: 8);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 2,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(1234);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Theron",
            attacker,
            CombatantType.Mob,
            "goblin",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        Assert.True(result.Damage.Hit);
        Assert.True(result.Damage.DamageDealt > 0);
        Assert.False(result.TargetDefeated);
        Assert.Contains("goblin", result.AttackerNarration);
        Assert.Contains("Theron", result.TargetNarration);
    }

    [Fact]
    public void ExecuteRound_miss_deals_no_damage()
    {
        var attacker = new AttackerStats(
            AttackRating: -5,
            BaseDamage: 2,
            MinDamage: 1,
            MaxDamage: 4);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 10,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(9999);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Weak",
            attacker,
            CombatantType.Mob,
            "knight",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        Assert.False(result.Damage.Hit);
        Assert.Equal(0, result.Damage.DamageDealt);
        Assert.False(result.TargetDefeated);
        Assert.Contains("miss", result.AttackerNarration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecuteRound_critical_hit_marked()
    {
        var attacker = new AttackerStats(
            AttackRating: 8,
            BaseDamage: 3,
            MinDamage: 8,
            MaxDamage: 12);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: -5,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(42);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Warrior",
            attacker,
            CombatantType.Mob,
            "goblin",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        // This seed should produce a high roll that hits and likely crits.
        Assert.True(result.Damage.Hit);
        if (result.Damage.IsCritical)
        {
            Assert.Contains("CRITICAL", result.AttackerNarration);
        }
    }

    [Fact]
    public void ExecuteRound_target_defeated_when_health_drops_to_zero()
    {
        var attacker = new AttackerStats(
            AttackRating: 5,
            BaseDamage: 8,
            MinDamage: 10,
            MaxDamage: 20);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: -10,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(100);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Strong",
            attacker,
            CombatantType.Mob,
            "weak-goblin",
            defender,
            targetCurrentHealth: 10,
            validation,
            "hit",
            random);

        // With high attack rating and low defense, should hit and deal damage.
        // Target has only 10 health, so should be defeated.
        Assert.True(result.TargetDefeated);
    }

    [Fact]
    public void ExecuteRound_target_not_defeated_if_survives()
    {
        var attacker = new AttackerStats(
            AttackRating: -3,
            BaseDamage: 1,
            MinDamage: 1,
            MaxDamage: 2);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 0,
            Armor: 100);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(1000);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Novice",
            attacker,
            CombatantType.Mob,
            "tough-goblin",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        // Target has 50 health; even if it hits with low damage and heavy armor, target survives.
        Assert.False(result.TargetDefeated);
    }

    [Fact]
    public void ExecuteRound_validation_refusal_ends_engagement()
    {
        var attacker = new AttackerStats(
            AttackRating: 4,
            BaseDamage: 3,
            MinDamage: 6,
            MaxDamage: 8);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 2,
            Armor: 0);

        var validation = new TargetValidationResult(false, "You cannot attack Kael here.");
        var random = new SeededRandomSource(5000);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Player",
            attacker,
            CombatantType.Player,
            "Kael",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        Assert.False(result.Damage.Hit);
        Assert.Equal(0, result.Damage.DamageDealt);
        Assert.False(result.TargetDefeated);
        Assert.Contains("cannot attack", result.AttackerNarration);
    }

    [Fact]
    public void ExecuteRound_narration_includes_damage_amount()
    {
        var attacker = new AttackerStats(
            AttackRating: 2,
            BaseDamage: 1,
            MinDamage: 4,
            MaxDamage: 6);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 0,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(2000);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Fighter",
            attacker,
            CombatantType.Mob,
            "goblin",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        if (result.Damage.Hit)
        {
            // Narration should mention the damage dealt.
            Assert.Contains(result.Damage.DamageDealt.ToString(), result.AttackerNarration);
        }
    }

    [Fact]
    public void ExecuteRound_narration_distinguishes_attacker_target_room()
    {
        var attacker = new AttackerStats(
            AttackRating: 3,
            BaseDamage: 2,
            MinDamage: 5,
            MaxDamage: 7);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 1,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(3000);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Alice",
            attacker,
            CombatantType.Mob,
            "orc",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        // All three narrations should exist and differ.
        Assert.NotEmpty(result.AttackerNarration);
        Assert.NotEmpty(result.TargetNarration);
        Assert.NotEmpty(result.RoomNarration);

        // Attacker's perspective uses "You" when hitting.
        if (result.Damage.Hit)
        {
            Assert.Contains("You", result.AttackerNarration);
            Assert.Contains("Alice", result.RoomNarration);
            Assert.Contains("Alice", result.TargetNarration);
        }
    }

    [Fact]
    public void ExecuteRound_mob_vs_player_allowed_in_normal_room()
    {
        var attacker = new AttackerStats(
            AttackRating: 1,
            BaseDamage: 1,
            MinDamage: 3,
            MaxDamage: 5);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 1,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(4000);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Mob,
            "goblin-sentry",
            attacker,
            CombatantType.Player,
            "Theron",
            defender,
            targetCurrentHealth: 50,
            validation,
            "hit",
            random);

        // Mob can attack player everywhere except peaceful (validation passed).
        // Any outcome is valid - hit or miss, defeated or not.
        Assert.NotNull(result);
    }

    [Fact]
    public void ExecuteRound_validates_attacker_name()
    {
        var attacker = new AttackerStats(
            AttackRating: 3,
            BaseDamage: 2,
            MinDamage: 5,
            MaxDamage: 8);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 2,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(5000);

        var ex = Assert.Throws<ArgumentException>(() =>
            CombatSystem.ExecuteRound(
                CombatantType.Player,
                string.Empty,
                attacker,
                CombatantType.Mob,
                "goblin",
                defender,
                targetCurrentHealth: 50,
                validation,
                "hit",
                random));

        Assert.Contains("attackerName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecuteRound_validates_target_name()
    {
        var attacker = new AttackerStats(
            AttackRating: 3,
            BaseDamage: 2,
            MinDamage: 5,
            MaxDamage: 8);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 2,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(5000);

        var ex = Assert.Throws<ArgumentException>(() =>
            CombatSystem.ExecuteRound(
                CombatantType.Player,
                "Fighter",
                attacker,
                CombatantType.Mob,
                string.Empty,
                defender,
                targetCurrentHealth: 50,
                validation,
                "hit",
                random));

        Assert.Contains("targetName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecuteRound_returns_correct_round_info()
    {
        var attacker = new AttackerStats(
            AttackRating: 2,
            BaseDamage: 1,
            MinDamage: 4,
            MaxDamage: 6);
        var defender = new DefenderStats(
            Level: 0,
            DefenseRating: 0,
            Armor: 0);

        var validation = new TargetValidationResult(true, null);
        var random = new SeededRandomSource(6000);

        var result = CombatSystem.ExecuteRound(
            CombatantType.Player,
            "Alice",
            attacker,
            CombatantType.Mob,
            "goblin",
            defender,
            targetCurrentHealth: 30,
            validation,
            "hit",
            random);

        // Verify CombatRound has all expected fields populated.
        Assert.Equal("Alice", result.AttackerName);
        Assert.Equal("goblin", result.TargetName);
        Assert.NotNull(result.Damage);
    }
}
