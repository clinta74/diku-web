using System.Globalization;
using DikuWeb.Playtest;
using DikuWeb.Playtest.Building;
using DikuWeb.Playtest.Load;
using DikuWeb.Playtest.Plans;
using DikuWeb.Playtest.Recording;
using DikuWeb.Playtest.Running;
using DikuWeb.Playtest.Session;
using DikuWeb.Playtest.Targets;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Options.Usage);
    return 0;
}

Options options;

try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Options.Usage);
    return 2;
}

if (options.Hosted)
{
    Console.Error.WriteLine("--hosted is not built yet. Use --server <url> for now.");
    return 2;
}

IReadOnlyList<PlanDocument> plans;

try
{
    plans = options.Plans is null
        ? [BuiltInPlans.Smoke()]
        : PlanLoader.LoadAll(options.Plans);
}
catch (PlaytestException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    // A run cut short is still worth reading, so Ctrl-C unwinds rather than killing the process:
    // the streams close, the transcripts are written, and the report says it was interrupted.
    e.Cancel = true;
    stopping.Cancel();
};

await using var target = new RemoteTarget(options.Server!, options.Admin);

Console.WriteLine($"Playing against {target.Describe()}");
Console.WriteLine($"{plans.Count} plan(s): {string.Join(", ", plans.Select(p => p.Name))}");
Console.WriteLine();

var startedAt = DateTimeOffset.UtcNow;
var runDirectory = Path.Combine(
    options.Output,
    startedAt.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", CultureInfo.InvariantCulture));

Directory.CreateDirectory(runDirectory);

// Before anybody plays, not per plan. The content a plan needs is world state: two plans wanting
// the same smith should find one smith, and a plan that walks past another plan's fixture should
// see it. Doing this up front also means the whole answer to "is this world ready" arrives in one
// block a reader can check, rather than dribbling out between transcripts.
var fixtureLog = await ProvisionAsync(target, plans, options, stopping.Token);

await File.WriteAllTextAsync(
    Path.Combine(runDirectory, "fixtures.log"),
    string.Join(Environment.NewLine, fixtureLog),
    CancellationToken.None);

// A load run is a different question with the same apparatus, so it forks here rather than
// threading a flag through every plan: it holds one plan open at scale and reports the server's
// pulse histogram, where an ordinary run plays every plan once and reports transcripts.
if (options.IsLoadRun)
{
    if (plans.Count > 1)
    {
        Console.Error.WriteLine(
            $"--sessions takes one plan; {plans.Count} were given. Two hundred sessions playing "
            + "different plans measures a mixture nobody can reason about afterwards.");

        return 2;
    }

    var loadPlan = plans[0];

    var load = new LoadRunner(
        target,
        options.Metrics ?? options.Server!,
        new LoadSettings
        {
            Sessions = options.Sessions,
            Ramp = options.Ramp,
            Hold = options.Hold,
        });

    LoadOutcome result;

    try
    {
        result = await load.RunAsync(loadPlan, stopping.Token);
    }
    catch (PlaytestException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    var summary = LoadReport.Build(result);
    Console.Write(summary);

    await File.WriteAllTextAsync(
        Path.Combine(runDirectory, "load.txt"), summary, CancellationToken.None);

    await File.WriteAllTextAsync(
        Path.Combine(runDirectory, "load.json"), LoadReport.Json(result), CancellationToken.None);

    // The observed replica's transcript, in full. The histogram says whether the loop kept up;
    // this says whether the game was still playable while it did, which no percentile can.
    await File.WriteAllTextAsync(
        Path.Combine(runDirectory, "observed.log"),
        TranscriptWriter.Interleaved(result.Observed, [.. loadPlan.Cast.Select(c => c.Name)]),
        CancellationToken.None);

    if (result.ObservedOutcome is { } observed)
    {
        Console.WriteLine(
            $"  observed session   {observed.Met} met, {observed.Unmet} unmet"
            + (observed.Problems.Count > 0 ? $", {observed.Problems.Count} problem(s)" : string.Empty));

        foreach (var problem in observed.Problems.Take(5))
        {
            Console.WriteLine($"    ! {problem}");
        }
    }

    foreach (var failure in result.Sessions.Where(s => !s.Arrived).Take(5))
    {
        Console.WriteLine($"    ! session {failure.Replica} never arrived: {failure.Failure}");
    }

    if (!options.NoCleanup)
    {
        var made = result.Sessions
            .Where(s => s.Outcome is not null)
            .SelectMany(s => s.Outcome!.CharacterNames)
            .ToList();

        var sweep = new Transcript();
        Console.WriteLine();
        Console.WriteLine(await Janitor.SweepAsync(target, sweep, made, CancellationToken.None));

        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "cleanup.log"),
            TranscriptWriter.Interleaved(sweep, ["Janitor"]),
            CancellationToken.None);
    }

    Console.WriteLine();
    Console.WriteLine(new Uri(Path.GetFullPath(runDirectory)).AbsoluteUri);

    // Zero regardless, for the same reason an ordinary run exits zero: this is a measurement, and
    // a measurement that exits non-zero because the answer was disappointing is a test suite
    // wearing a disguise. The verdict line is the result; read it.
    return 0;
}

var reported = new List<ReportedPlan>();

foreach (var plan in plans)
{
    Console.WriteLine($"── {plan.Name}");

    // A transcript each, because a plan is the unit somebody reads. One shared record across
    // plans would put an unrelated plan's actors in the interleaved view and make the relative
    // clock meaningless — it would be counting from the start of the whole run rather than from
    // the start of the thing being read.
    var transcript = new Transcript();
    var runner = new PlanRunner(target, transcript, new RunSettings());

    var outcome = await runner.RunAsync(plan, stopping.Token);
    reported.Add(new ReportedPlan(outcome, transcript, plan.About));

    var planDirectory = Path.Combine(runDirectory, Slug(plan.Name));
    Directory.CreateDirectory(planDirectory);

    var actors = outcome.Actors;

    await File.WriteAllTextAsync(
        Path.Combine(planDirectory, "interleaved.log"),
        TranscriptWriter.Interleaved(transcript, actors),
        CancellationToken.None);

    foreach (var actor in actors)
    {
        await File.WriteAllTextAsync(
            Path.Combine(planDirectory, $"{Slug(actor)}.log"),
            TranscriptWriter.ForActor(transcript, actor),
            CancellationToken.None);
    }

    if (options.Follow)
    {
        Console.WriteLine(TranscriptWriter.Interleaved(transcript, actors));
    }

    Console.WriteLine(Summarise(outcome));
    Console.WriteLine();

    if (stopping.IsCancellationRequested)
    {
        break;
    }
}

// After every plan, and only ever the characters this run made. A janitor per plan would leave one
// behind each time, because the verb refuses to delete the character being played.
if (!options.NoCleanup)
{
    var made = reported.SelectMany(r => r.Outcome.CharacterNames).ToList();
    var transcript = new Transcript();

    Console.WriteLine(await Janitor.SweepAsync(target, transcript, made, CancellationToken.None));

    await File.WriteAllTextAsync(
        Path.Combine(runDirectory, "cleanup.log"),
        TranscriptWriter.Interleaved(transcript, ["Janitor"]),
        CancellationToken.None);
}

var indexPath = Path.Combine(runDirectory, "index.html");

await File.WriteAllTextAsync(
    indexPath,
    HtmlReporter.Build(startedAt, target.Describe(), reported),
    CancellationToken.None);

await File.WriteAllTextAsync(
    Path.Combine(runDirectory, "run.json"),
    JsonReporter.Build(startedAt, target.Describe(), reported),
    CancellationToken.None);

var needsReview = reported.Count(o => o.Outcome.NeedsReview);

Console.WriteLine(needsReview == 0
    ? "Nothing flagged. Read it anyway — that is what it is for."
    : $"{needsReview} of {reported.Count} plan(s) flagged something.");

Console.WriteLine(new Uri(Path.GetFullPath(indexPath)).AbsoluteUri);

// Zero regardless. A playtest is a recording, not a gate: exiting non-zero because a game did
// something surprising would make this a second test suite, and CI would start ignoring it.
return 0;

/// <summary>
/// Puts every plan's <c>world:</c> content in the world, and says what it found and what it made.
/// </summary>
/// <remarks>
/// Never fatal. A world that could not be authored is one whose plans play against what is
/// actually there, and the transcript of a player standing in a room the fixture never reached is
/// worth more than a run that refused to start. What must not happen is that going *unsaid* — this
/// prints, and writes `fixtures.log`, so a plan reading oddly has its explanation in the same run
/// directory.
/// </remarks>
static async Task<IReadOnlyList<string>> ProvisionAsync(
    IGameTarget target,
    IReadOnlyList<PlanDocument> plans,
    Options options,
    CancellationToken cancellationToken)
{
    var wanted = plans
        .Where(p => p.World is { IsEmpty: false })
        .Select(p => (p.Name, World: p.World!))
        .ToList();

    if (wanted.Count == 0)
    {
        return [];
    }

    if (options.NoFixtures)
    {
        var skipped = $"--no-fixtures: playing the world as it stands, "
            + $"and {wanted.Count} plan(s) declare content they may not find.";

        Console.WriteLine(skipped);
        Console.WriteLine();
        return [skipped];
    }

    var access = await target.BuilderAccessAsync(cancellationToken);

    if (!access.IsGranted)
    {
        var refused = $"Fixtures not built: {access.Reason}";

        Console.WriteLine(refused);
        Console.WriteLine();
        return [refused];
    }

    var provisioner = new FixtureProvisioner(access.Client!);
    var lines = new List<string>();
    var outcomes = new List<FixtureOutcome>();

    foreach (var (name, world) in wanted)
    {
        foreach (var outcome in await provisioner.EnsureAsync(world, cancellationToken))
        {
            outcomes.Add(outcome);
            lines.Add($"{name}: {outcome}");
        }
    }

    var made = outcomes.Count(o => o.State == FixtureState.Made);
    var blocked = outcomes.Count(o => o.State == FixtureState.Blocked);

    Console.WriteLine(
        $"World: {outcomes.Count - made - blocked} fixture(s) already there, {made} made"
        + (blocked > 0 ? $", {blocked} REFUSED" : string.Empty));

    // Only the interesting halves on the console. Everything is in fixtures.log either way, and a
    // list of twenty things that were already fine is a list nobody reads.
    foreach (var line in lines.Where(
        l => l.Contains(": made ", StringComparison.Ordinal)
            || l.Contains("COULD NOT", StringComparison.Ordinal)))
    {
        Console.WriteLine($"  {line}");
    }

    // A spawner is a rule, not an instance — the loop's spawn sweep is what stands a mob in a
    // room, and it runs every 60 pulses (GameTiming.SpawnSweepPulses, 15 s). The first fixtured
    // run beat it and the transcript read "You don't see 'rat' here." followed two seconds later
    // by "A rat appears." The number is duplicated rather than referenced because the apparatus
    // is a client and does not see Engine; if it ever drifts, this waits too little and a plan
    // says so in the only way it can.
    if (outcomes.Any(o => o.SpawnPending))
    {
        var wait = TimeSpan.FromSeconds(17);

        Console.WriteLine(
            $"  waiting {wait.TotalSeconds:0}s for the spawn sweep to stand the new content up");

        lines.Add($"waited {wait.TotalSeconds:0}s for the spawn sweep after creating spawners");
        await Task.Delay(wait, cancellationToken);
    }

    Console.WriteLine();
    return lines;
}

static string Slug(string name)
{
    var slug = new string([.. name.ToLowerInvariant()
        .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);

    while (slug.Contains("--", StringComparison.Ordinal))
    {
        slug = slug.Replace("--", "-", StringComparison.Ordinal);
    }

    return slug.Trim('-') is { Length: > 0 } trimmed ? trimmed : "plan";
}

static string Summarise(PlanOutcome outcome)
{
    var parts = new List<string> { $"{outcome.Met} met" };

    if (outcome.Unmet > 0)
    {
        parts.Add($"{outcome.Unmet} UNMET");
    }

    foreach (var problem in outcome.Problems)
    {
        parts.Add($"! {problem}");
    }

    return "   " + string.Join(", ", parts);
}

/// <summary>
/// The plan used when none was given, so the apparatus can prove itself against a new server
/// without anybody writing a file first.
/// </summary>
/// <remarks>
/// Deliberately the dullest thing a player does. It touches every moving part — registration,
/// character creation, entering the world, the SSE stream, a command, and the answer arriving
/// asynchronously over that same stream — so if this reads like a real session, the apparatus
/// works and everything else is a matter of describing scenarios rather than performing them.
/// </remarks>
internal static class BuiltInPlans
{
    public static PlanDocument Smoke()
    {
        var plan = new PlanDocument
        {
            Name = "smoke",
            About = "One character arrives, looks around, and checks who else is here.",
        };

        plan.Cast.Add(new CastMember { Name = "Theron", Path = "Warden" });

        plan.Steps.Add(new PlanStep
        {
            Actor = "Theron",
            Note = "Arrival should describe the room without being asked.",
            Expect = "Exits:",
        });

        plan.Steps.Add(new PlanStep
        {
            Actor = "Theron",
            Do = "look",
            Wait = new WaitSpec { Text = "Exits:" },
            Expect = "Exits:",
        });

        plan.Steps.Add(new PlanStep
        {
            Actor = "Theron",
            Do = "who",
            Wait = new WaitSpec { Text = "online" },
            Expect = "Theron",
        });

        return plan;
    }
}
