using System.Globalization;
using DikuWeb.Playtest;
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
