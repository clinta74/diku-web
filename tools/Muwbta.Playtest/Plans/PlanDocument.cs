namespace Muwbta.Playtest.Plans;

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

/// <summary>
/// Content a plan needs, checked before anyone plays and created where it is missing.
/// </summary>
/// <remarks>
/// <b>Created through the builder API, never by writing rows.</b> The world a running server plays
/// is an in-memory snapshot owned by one loop thread and loaded at boot (PLAN.md §2.1); a builder
/// edit is a mutation queued into that loop and written through to Postgres afterwards. SQL run
/// against the database therefore reaches the *storage* and not the *world* — the fixture would be
/// invisible until a restart, which is the worst possible failure here, because the plan would run
/// against a server that genuinely does not have the mob and report it as missing content. Using
/// the same door the builder uses is also what keeps the apparatus a client (PLAYTEST.md).
///
/// Fixtures are matched by key and are idempotent: what is already there is left exactly as it is
/// rather than reconciled. A plan must never quietly re-point somebody's hand-built content at its
/// own numbers — if a `village-smith` already exists, the honest outcome is to play against it and
/// say in the report that the fixture was found rather than made.
/// </remarks>
public sealed class WorldFixtures
{
    /// <summary>Zones this plan's rooms live in. Made before the rooms that name them.</summary>
    public IList<ZoneFixture> Zones { get; init; } = [];

    /// <summary>Rooms this plan walks into. Verified; dug only when told where from.</summary>
    public IList<RoomFixture> Rooms { get; init; } = [];

    public IList<MobFixture> Mobs { get; init; } = [];

    public IList<ItemFixture> Items { get; init; } = [];

    /// <summary>True when there is nothing here to do.</summary>
    public bool IsEmpty =>
        Zones.Count == 0 && Rooms.Count == 0 && Mobs.Count == 0 && Items.Count == 0;
}

/// <summary>
/// A zone a plan's rooms belong to.
/// </summary>
/// <remarks>
/// Left at the default multipliers unless a plan says otherwise, which is the conservative
/// choice: a zone is the unit difficulty is authored in (§4.4), and a fixture that quietly made
/// somewhere 2.5x would change what every plan in it is measuring.
/// </remarks>
public sealed class ZoneFixture
{
    /// <summary>The <c>world.zone</c> key.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

/// <summary>
/// A room the plan needs to exist.
/// </summary>
/// <remarks>
/// <b>Verified by default, and dug only when the plan says where from.</b> A room created out of
/// nothing has no exits leading to it, so it is a room no player can reach and no plan can walk
/// into — a fixture that reports success and leaves the plan just as broken. Digging is how the
/// builder makes a room for exactly this reason (§7.6): the exit and the room arrive together.
/// </remarks>
public sealed class RoomFixture
{
    /// <summary>The full <c>world.zone.room</c> key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>An existing room to dig from. Null means verify only.</summary>
    public string? From { get; set; }

    /// <summary>Which way the new room lies from <see cref="From"/>.</summary>
    public string? Direction { get; set; }

    /// <summary>
    /// The zone the dug room belongs to, when it is not the one dug from.
    /// </summary>
    /// <remarks>
    /// This is how a plan crosses a zone boundary, which is a thing worth being able to play
    /// through: the exit is an ordinary exit and the difficulty on the far side of it is not.
    /// </remarks>
    public string? Zone { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }
}

/// <summary>A mob template, and a sentinel spawner standing one of them in a room.</summary>
public sealed class MobFixture
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    /// <summary>Where one of these stands. Null creates the template and no spawner.</summary>
    public string? Room { get; set; }

    public int Health { get; set; } = 20;

    public int Level { get; set; } = 1;

    public int Xp { get; set; }

    public int Gold { get; set; }

    /// <summary>passive, aggressive, or npc. Anything a plan stands next to wants npc.</summary>
    public string Disposition { get; set; } = "passive";

    /// <summary>Whether players can <c>list</c>, <c>buy</c>, and <c>sell</c> here.</summary>
    public bool Shopkeeper { get; set; }

    /// <summary>Item template keys this shop stocks.</summary>
    public IList<string> Sells { get; init; } = [];

    /// <summary>How far over base value it prices them: 0.1 is 1.1x (PLAN.md §4.13).</summary>
    public decimal Markup { get; set; }

    /// <summary>How many stand there. One unless a plan needs a crowd.</summary>
    public int Count { get; set; } = 1;
}

/// <summary>An item template, and optionally a spawner laying some in a room.</summary>
public sealed class ItemFixture
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    /// <summary>Where these lie on the ground. Null creates the template and no spawner.</summary>
    public string? Room { get; set; }

    /// <summary>
    /// Where it can be equipped. A plan writes one slot because a fixture is a prop, not content -
    /// the multi-slot and two-handed cases are authored in the builder and pinned by its own tests.
    /// </summary>
    public string? Slot { get; set; }

    public int Value { get; set; }

    /// <summary>Bound to a quest: cannot be sold or destroyed (PLAN.md §4.9).</summary>
    public bool QuestItem { get; set; }

    /// <summary>How many lie there. An item spawner counts by room, so this is a resupply target.</summary>
    public int Count { get; set; } = 1;
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
