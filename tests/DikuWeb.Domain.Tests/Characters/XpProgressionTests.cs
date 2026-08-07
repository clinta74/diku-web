using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Characters;

public sealed class XpProgressionTests
{
    [Fact]
    public void XpForLevel_level_1_requires_zero_xp()
    {
        Assert.Equal(0, XpProgression.XpForLevel(1));
    }

    [Fact]
    public void XpForLevel_level_2_requires_1000_xp()
    {
        Assert.Equal(1000, XpProgression.XpForLevel(2));
    }

    [Fact]
    public void XpForLevel_level_3_requires_3000_xp()
    {
        Assert.Equal(3000, XpProgression.XpForLevel(3));
    }

    [Fact]
    public void XpForLevel_level_5_requires_10000_xp()
    {
        Assert.Equal(10000, XpProgression.XpForLevel(5));
    }

    [Fact]
    public void XpForLevel_level_50_requires_1225000_xp()
    {
        Assert.Equal(1225000, XpProgression.XpForLevel(50));
    }

    [Fact]
    public void XpForLevel_out_of_range_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XpProgression.XpForLevel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => XpProgression.XpForLevel(51));
    }

    [Fact]
    public void LevelForXp_negative_xp_is_level_1()
    {
        Assert.Equal(1, XpProgression.LevelForXp(-100));
    }

    [Fact]
    public void LevelForXp_zero_xp_is_level_1()
    {
        Assert.Equal(1, XpProgression.LevelForXp(0));
    }

    [Fact]
    public void LevelForXp_999_xp_is_level_1()
    {
        Assert.Equal(1, XpProgression.LevelForXp(999));
    }

    [Fact]
    public void LevelForXp_1000_xp_is_level_2()
    {
        Assert.Equal(2, XpProgression.LevelForXp(1000));
    }

    [Fact]
    public void LevelForXp_3000_xp_is_level_3()
    {
        Assert.Equal(3, XpProgression.LevelForXp(3000));
    }

    [Fact]
    public void LevelForXp_10000_xp_is_level_5()
    {
        Assert.Equal(5, XpProgression.LevelForXp(10000));
    }

    [Fact]
    public void LevelForXp_caps_at_max_level()
    {
        var maxXp = XpProgression.XpForLevel(50) + 1000000;
        Assert.Equal(50, XpProgression.LevelForXp(maxXp));
    }

    [Fact]
    public void LevelForXp_thresholds_are_monotonic()
    {
        for (var level = 1; level < 50; level++)
        {
            var xp1 = XpProgression.XpForLevel(level);
            var xp2 = XpProgression.XpForLevel(level + 1);
            Assert.True(xp1 < xp2, $"Level {level} ({xp1}) should require less XP than level {level + 1} ({xp2})");
        }
    }

    [Fact]
    public void XpForLevel_and_LevelForXp_are_inverse()
    {
        for (var level = 1; level <= 50; level++)
        {
            var xp = XpProgression.XpForLevel(level);
            Assert.Equal(level, XpProgression.LevelForXp(xp));
        }
    }
}
