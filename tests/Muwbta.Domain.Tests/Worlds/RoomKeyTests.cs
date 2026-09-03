using Muwbta.Domain.Worlds;

namespace Muwbta.Domain.Tests.Worlds;

public sealed class RoomKeyTests
{
    [Fact]
    public void Parses_the_three_segments()
    {
        var key = RoomKey.Parse("aldenmoor.millbrook.north-gate");

        Assert.Equal("aldenmoor", key.World);
        Assert.Equal("millbrook", key.Zone);
        Assert.Equal("north-gate", key.Room);
    }

    [Fact]
    public void Round_trips_through_its_string_form()
    {
        const string Text = "aldenmoor.millbrook.north-gate";

        Assert.Equal(Text, RoomKey.Parse(Text).ToString());
    }

    [Fact]
    public void Exposes_the_owning_zone_key()
    {
        // Used constantly: spawners, level ranges, and multipliers are all zone-scoped.
        var key = RoomKey.Parse("aldenmoor.millbrook.north-gate");

        Assert.Equal("aldenmoor.millbrook", key.ZoneKey);
        Assert.Equal("aldenmoor", key.WorldKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("millbrook.north-gate")]          // two segments: the world is missing
    [InlineData("aldenmoor.millbrook")]           // two segments
    [InlineData("a.b.c.d")]                       // four segments
    [InlineData("Aldenmoor.millbrook.north-gate")] // uppercase
    [InlineData("aldenmoor.mill brook.gate")]     // space
    [InlineData("aldenmoor..north-gate")]         // empty segment
    [InlineData("aldenmoor.millbrook.-gate")]     // leading hyphen
    [InlineData("aldenmoor.millbrook.gate-")]     // trailing hyphen
    [InlineData("aldenmoor.millbrook.north_gate")] // underscore
    public void Rejects_malformed_keys(string input)
    {
        Assert.False(RoomKey.TryParse(input, out _));
        Assert.Throws<FormatException>(() => RoomKey.Parse(input));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.False(RoomKey.TryParse(null, out _));
    }

    [Fact]
    public void Rejects_keys_over_the_column_length()
    {
        // The column is varchar(128); a longer key would throw at insert time rather than
        // at the boundary where the mistake was actually made.
        var tooLong = $"world.zone.{new string('a', RoomKey.MaxLength)}";

        Assert.False(RoomKey.TryParse(tooLong, out _));
    }

    [Fact]
    public void Equality_is_by_value()
    {
        var a = RoomKey.Parse("aldenmoor.millbrook.north-gate");
        var b = RoomKey.Create("aldenmoor", "millbrook", "north-gate");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
