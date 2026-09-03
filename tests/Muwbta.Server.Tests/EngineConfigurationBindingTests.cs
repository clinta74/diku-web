using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Microsoft.Extensions.Configuration;

namespace Muwbta.Server.Tests;

/// <summary>
/// Why <c>Program.cs</c> reads the <c>Engine</c> section key by key instead of calling
/// <c>Bind</c>.
/// </summary>
/// <remarks>
/// These assert framework behaviour rather than ours, which is usually a waste of a test. They
/// earn their place because the decision they explain looks arbitrary in the source — "why not
/// just Bind?" — and because the first answer written there was wrong. Binding was claimed to
/// throw on <c>StartingRoom</c>; it does not, it ignores it. The real reasons are below, and they
/// are the kind that change silently when a dependency is upgraded.
/// </remarks>
public sealed class EngineConfigurationBindingTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void Bind_silently_ignores_the_starting_room()
    {
        // The first reason. RoomKey is a readonly record struct with no type converter, so the
        // binder cannot make one from a string — and rather than saying so, it leaves the property
        // as it found it. Binding would therefore have left Engine__StartingRoom exactly as it
        // was: a setting that reads as configured and does nothing, which is the whole complaint
        // this change set out to fix.
        var configuration = Config(("Engine:StartingRoom", "elsewhere.otherzone.otherroom"));
        var options = new EngineOptions();

        var thrown = Record.Exception(() => configuration.GetSection("Engine").Bind(options));

        Assert.Null(thrown);
        Assert.Equal("aldenmoor.millbrook.north-gate", options.StartingRoom.ToString());
    }

    [Fact]
    public void Bind_throws_on_a_scalar_it_cannot_convert()
    {
        // The second reason, and the sharper one. A typo in any numeric setting takes the host
        // down at startup rather than being ignored in favour of a working default. Fail-fast is a
        // defensible choice for a value with no default; it is the wrong one for a window that
        // already has a correct one.
        var configuration = Config(("Engine:LinkDeadGraceSeconds", "five minutes"));

        var thrown = Record.Exception(() => configuration.GetSection("Engine").Bind(new EngineOptions()));

        Assert.IsType<InvalidOperationException>(thrown);
    }

    [Fact]
    public void Bind_does_handle_a_well_formed_scalar()
    {
        // Stated so the two above are not mistaken for "Bind does not work". It works; it is the
        // failure modes that are wrong for this section.
        var configuration = Config(("Engine:LinkDeadGraceSeconds", "300"));
        var options = new EngineOptions();

        configuration.GetSection("Engine").Bind(options);

        Assert.Equal(300, options.LinkDeadGraceSeconds);
    }

    [Fact]
    public void The_value_the_compose_file_carried_was_never_a_room_key()
    {
        // docker-compose.prod.yml set Engine__StartingRoom to this. Nothing ever read it, so
        // nothing ever said so.
        Assert.False(RoomKey.TryParse("hall@0.0.0", out _));
    }

    [Fact]
    public void Reading_the_keys_explicitly_takes_what_is_valid_and_ignores_what_is_not()
    {
        // The shape Program.cs uses, against the compose file as it actually was: a usable grace
        // window and an unusable room. The server keeps its default room and boots.
        var section = Config(
            ("Engine:StartingRoom", "hall@0.0.0"),
            ("Engine:LinkDeadGraceSeconds", "300")).GetSection("Engine");

        var options = new EngineOptions();

        if (int.TryParse(section["LinkDeadGraceSeconds"], out var seconds) && seconds > 0)
        {
            options.LinkDeadGraceSeconds = seconds;
        }

        if (RoomKey.TryParse(section["StartingRoom"], out var room))
        {
            options.StartingRoom = room;
        }

        Assert.Equal(300, options.LinkDeadGraceSeconds);
        Assert.Equal("aldenmoor.millbrook.north-gate", options.StartingRoom.ToString());
    }

    [Fact]
    public void And_takes_a_starting_room_that_is_valid()
    {
        // The half that Bind could not do at all: this is the only path on which the setting has
        // ever worked.
        var section = Config(("Engine:StartingRoom", "elsewhere.otherzone.otherroom")).GetSection("Engine");
        var options = new EngineOptions();

        if (RoomKey.TryParse(section["StartingRoom"], out var room))
        {
            options.StartingRoom = room;
        }

        Assert.Equal("elsewhere.otherzone.otherroom", options.StartingRoom.ToString());
    }
}
