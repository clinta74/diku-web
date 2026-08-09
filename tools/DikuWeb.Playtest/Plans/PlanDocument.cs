namespace DikuWeb.Playtest.Plans;

/// <summary>
/// A playtest plan: who is in it, what the world needs to contain, and what they do.
/// </summary>
/// <remarks>
/// Deliberately not a test. A plan describes a <em>sequence a player would perform</em> and its
/// product is a transcript somebody reads; the expectations in it are observations recorded
/// alongside that transcript, never a verdict on the run. The existing suite already asserts
/// properties one at a time and does it better than this ever could — what it cannot do is notice
/// that <em>"Your Kick takes effect!"</em> reads like nothing happened, because every assertion
/// about it passed.
/// </remarks>
public sealed class PlanDocument
{
    /// <summary>What this plan is called, in a report.</summary>
    public string Name { get; set; } = "unnamed";

    /// <summary>Prose for the reviewer: what this plan is for, and what to look for.</summary>
    public string? About { get; set; }

    /// <summary>Content this plan needs to exist before anyone can play it.</summary>
    public WorldFixtures? World { get; set; }

    /// <summary>The characters to create and drive.</summary>
    public IList<CastMember> Cast { get; init; } = [];

    /// <summary>What they do, in order.</summary>
    public IList<PlanStep> Steps { get; init; } = [];

    /// <summary>Where this plan was loaded from, for the report.</summary>
    public string? SourcePath { get; set; }
}

/// <summary>One character the plan drives.</summary>
public sealed class CastMember
{
    /// <summary>
    /// What the plan calls them, and the name they get in the world where it is free.
    /// </summary>
    public string Name { get; set; } = "Actor";

    /// <summary>Warden, Adept, Shade, or Hallow.</summary>
    public string Path { get; set; } = "Warden";

    /// <summary>
    /// An account role this actor needs: Builder or Admin. Granted before anyone plays.
    /// </summary>
    /// <remarks>
    /// The only setup this model does, and everything else a plan needs is expressed as commands.
    /// There is deliberately no <c>level:</c> or <c>start:</c> here: a plan that wants a level-12
    /// Warden puts an Admin in its own cast and has them type <c>set Theron level 12</c>, and a
    /// plan that wants somebody in the tavern walks them there. Both then appear in the transcript
    /// as things that happened, which is what a reviewer needs — setup performed invisibly by the
    /// apparatus is setup nobody can check, and it would have needed engine features (an admin
    /// <c>goto</c>) that do not exist and should not be added for a test tool.
    /// </remarks>
    public string? Role { get; set; }
}

/// <summary>
/// One beat of the plan: optionally say something, optionally wait, optionally observe.
/// </summary>
/// <remarks>
/// The three collapse into one node because they are almost always the same beat — type a command,
/// wait for the answer, check the answer says what it should. Splitting them into three steps
/// would triple the length of every plan and put the wait and the expectation a screen away from
/// the command they belong to.
/// </remarks>
public sealed class PlanStep
{
    /// <summary>Who acts. Required unless this step is only a note or a <c>together</c> block.</summary>
    public string? Actor { get; set; }

    /// <summary>A marker in the transcript saying what the next stretch is meant to show.</summary>
    public string? Note { get; set; }

    /// <summary>What the player types.</summary>
    public string? Do { get; set; }

    /// <summary>What to wait for before observing.</summary>
    public WaitSpec? Wait { get; set; }

    /// <summary>Text that should appear. A string or a list of them.</summary>
    public object? Expect { get; set; }

    /// <summary>Text that should <em>not</em> appear. A string or a list of them.</summary>
    public object? ExpectNot { get; set; }

    /// <summary>Steps to run at the same time, for plans that need a race.</summary>
    public IList<PlanStep> Together { get; init; } = [];

    /// <summary>The <c>expect</c> entries, however they were written.</summary>
    public IReadOnlyList<string> Expectations => OneOrMany.Read(Expect);

    /// <summary>The <c>expect-not</c> entries, however they were written.</summary>
    public IReadOnlyList<string> Prohibitions => OneOrMany.Read(ExpectNot);
}

/// <summary>What a step waits for before it decides anything.</summary>
public sealed class WaitSpec
{
    /// <summary>Wait until this appears in the actor's output.</summary>
    public string? Text { get; set; }

    /// <summary>Wait this long regardless — for output that is defined by not arriving.</summary>
    public double? Seconds { get; set; }

    /// <summary>
    /// How long to wait for <see cref="Text"/> before giving up. Defaults to ten seconds.
    /// </summary>
    /// <remarks>
    /// A timeout is recorded and the plan carries on. Throwing would abandon the rest of the
    /// transcript, and the steps after a missed line are usually the ones that explain why it was
    /// missed.
    /// </remarks>
    public double? Timeout { get; set; }
}

/// <summary>Content a plan needs, created through the builder API before anyone plays.</summary>
public sealed class WorldFixtures
{
    public IList<MobFixture> Mobs { get; init; } = [];

    public IList<ItemFixture> Items { get; init; } = [];
}

/// <summary>A mob template plus one instance of it, standing somewhere.</summary>
public sealed class MobFixture
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Room { get; set; }

    public int Health { get; set; } = 20;

    public int Level { get; set; } = 1;

    public int Xp { get; set; }

    public int Gold { get; set; }
}

/// <summary>An item template plus one instance of it, lying somewhere.</summary>
public sealed class ItemFixture
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Room { get; set; }

    public string? Slot { get; set; }

    public int Value { get; set; }
}

/// <summary>
/// Reads a YAML value that may be written as one thing or as a list of them.
/// </summary>
/// <remarks>
/// <c>expect: "You walk east"</c> and <c>expect: ["a", "b"]</c> are both natural to write, and a
/// format that only accepted the list form would put brackets round the overwhelmingly common
/// single case. YamlDotNet will not coerce a scalar into a list, so the property is typed loosely
/// and normalised here rather than in eight call sites.
/// </remarks>
internal static class OneOrMany
{
    public static IReadOnlyList<string> Read(object? value) => value switch
    {
        null => [],
        string single => [single],
        IEnumerable<object> many => [.. many.Select(v => v?.ToString() ?? string.Empty)],
        _ => [value.ToString() ?? string.Empty],
    };
}
