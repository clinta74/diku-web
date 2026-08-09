using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Characters;

public sealed class PathGrowthTests
{
    [Fact]
    public void Warden_growth_favors_might_and_vitality()
    {
        var growth = PathGrowth.WardenGrowth;
        Assert.Equal(2, growth.Might);
        Assert.Equal(2, growth.Vitality);
        Assert.Equal(1, growth.Agility);
        Assert.Equal(0, growth.Insight);
        Assert.Equal(1, growth.Resolve);
    }

    [Fact]
    public void Adept_growth_favors_insight_and_resolve()
    {
        var growth = PathGrowth.AdeptGrowth;
        Assert.Equal(0, growth.Might);
        Assert.Equal(2, growth.Insight);
        Assert.Equal(2, growth.Resolve);
        Assert.Equal(1, growth.Agility);
        Assert.Equal(1, growth.Vitality);
    }

    [Fact]
    public void Shade_growth_favors_agility_and_insight()
    {
        var growth = PathGrowth.ShadeGrowth;
        Assert.Equal(2, growth.Agility);
        Assert.Equal(2, growth.Insight);
        Assert.Equal(1, growth.Might);
        Assert.Equal(0, growth.Vitality);
        Assert.Equal(1, growth.Resolve);
    }

    [Fact]
    public void Hallow_growth_favors_insight_and_resolve()
    {
        var growth = PathGrowth.HallowGrowth;
        Assert.Equal(2, growth.Insight);
        Assert.Equal(2, growth.Resolve);
        Assert.Equal(1, growth.Vitality);
        Assert.Equal(1, growth.Agility);
        Assert.Equal(0, growth.Might);
    }

    [Fact]
    public void For_returns_correct_path_growth()
    {
        Assert.Equal(PathGrowth.WardenGrowth, PathGrowth.For(CharacterPath.Warden));
        Assert.Equal(PathGrowth.AdeptGrowth, PathGrowth.For(CharacterPath.Adept));
        Assert.Equal(PathGrowth.ShadeGrowth, PathGrowth.For(CharacterPath.Shade));
        Assert.Equal(PathGrowth.HallowGrowth, PathGrowth.For(CharacterPath.Hallow));
    }

    [Fact]
    public void ApplyTo_increases_attributes()
    {
        var attrs = AttributeSet.Baseline; // 10 / 10 / 10 / 10 / 10
        var growth = PathGrowth.WardenGrowth;

        growth.ApplyTo(ref attrs);

        Assert.Equal(12, attrs.Might);      // 10 + 2
        Assert.Equal(11, attrs.Agility);    // 10 + 1
        Assert.Equal(12, attrs.Vitality);   // 10 + 2
        Assert.Equal(10, attrs.Insight);    // 10 + 0
        Assert.Equal(11, attrs.Resolve);    // 10 + 1
    }

    [Fact]
    public void ApplyTo_clamps_at_max_value()
    {
        var attrs = new AttributeSet
        {
            Might = 19,
            Agility = 20,
            Vitality = 18,
            Insight = 20,
            Resolve = 20,
        };

        var growth = PathGrowth.WardenGrowth;
        growth.ApplyTo(ref attrs);

        Assert.Equal(20, attrs.Might);      // 19 + 2 clamped to 20
        Assert.Equal(20, attrs.Agility);    // 20 + 1 clamped to 20
        Assert.Equal(20, attrs.Vitality);   // 18 + 2 clamped to 20
        Assert.Equal(20, attrs.Insight);    // 20 + 0 stays at 20
        Assert.Equal(20, attrs.Resolve);    // 20 + 1 clamped to 20
    }

    [Fact]
    public void All_paths_total_6_points_per_level()
    {
        var paths = new[]
        {
            PathGrowth.WardenGrowth,
            PathGrowth.AdeptGrowth,
            PathGrowth.ShadeGrowth,
            PathGrowth.HallowGrowth
        };

        foreach (var growth in paths)
        {
            var total = growth.Might + growth.Agility + growth.Vitality + growth.Insight + growth.Resolve;
            Assert.Equal(6, total);
        }
    }
}
