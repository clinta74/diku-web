using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Playtest.Plans;
using DikuWeb.Playtest.Recording;
using DikuWeb.Playtest.Session;
using DikuWeb.Playtest.Targets;

namespace DikuWeb.Playtest.Running;

/// <summary>What one plan produced.</summary>
public sealed record PlanOutcome(
    string Name,
    string? SourcePath,
    IReadOnlyList<string> Actors,
    int Met,
    int Unmet,
    IReadOnlyList<string> Problems)
{
    /// <summary>Whether anything at all wants a human's attention.</summary>
    public bool NeedsReview => Unmet > 0 || Problems.Count > 0;

    /// <summary>
    /// The names the world actually gave this plan's cast, for the cleanup pass.
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="Actors"/>, which holds the names the <em>plan</em> used. A
    /// janitor deleting "Theron" would delete whoever happens to be called that; it has to delete
    /// "Theronqxbfm", which is the one this run created.
    /// </remarks>
    public IReadOnlyList<string> CharacterNames { get; init; } = [];
}

/// <summary>
/// Performs one plan against a target and records everything.
/// </summary>
/// <remarks>
/// <b>Nothing here throws on a disappointing result.</b> A missed expectation, a wait that timed
/// out, a level that could not be granted — all of them are recorded and the plan carries on. This
/// is the single most important decision in the apparatus: the steps after a missed line are
/// usually the ones that explain why it was missed, and a runner that abandoned the transcript at
/// the first surprise would throw away the evidence it exists to collect.
///
/// Only a failure to <em>set the plan up at all</em> ends it early, because a cast that never
/// arrived has nothing to record.
/// </remarks>
public sealed class PlanRunner(IGameTarget target, Transcript transcript, RunSettings settings)
{
    public async Task<PlanOutcome> RunAsync(PlanDocument plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var problems = new List<string>();
        var actors = new Dictionary<string, Actor>(StringComparer.OrdinalIgnoreCase);
        var created = new List<string>();

        transcript.Add(Apparatus, EntryKind.Note, $"Plan: {plan.Name}");

        if (!string.IsNullOrWhiteSpace(plan.About))
        {
            transcript.Add(Apparatus, EntryKind.Meta, plan.About.Trim());
        }

        try
        {
            // Before anyone arrives, so the first step for each actor can see the arrival burst —
            // the welcome, the vitals, and the room description that the world sends unprompted.
            // "Entering describes the room without being asked" is a thing worth playtesting, and
            // a window that opened after it could never observe it.
            var since = plan.Cast.ToDictionary(
                c => c.Name, _ => transcript.Now, StringComparer.OrdinalIgnoreCase);

            foreach (var member in plan.Cast)
            {
                var actor = await CastAsync(member, problems, cancellationToken);
                actors[member.Name] = actor;
                created.Add(actor.CharacterName);
            }

            // Let the arrival burst land before the first command, so a plan's opening line is
            // not racing the world's.
            await Task.Delay(settings.SettleAfterArrival, cancellationToken);

            await RunStepsAsync(plan.Steps, actors, since, problems, cancellationToken);
        }
        catch (PlaytestException ex)
        {
            problems.Add(ex.Message);
            transcript.Add(Apparatus, EntryKind.Meta, $"plan stopped: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            problems.Add("The run was interrupted.");
            transcript.Add(Apparatus, EntryKind.Meta, "plan interrupted");
        }
        finally
        {
            foreach (var actor in actors.Values)
            {
                await actor.DisposeAsync();
            }
        }

        var observations = transcript.Entries.Where(e => e.Kind == EntryKind.Observation).ToList();

        return new PlanOutcome(
            plan.Name,
            plan.SourcePath,
            [.. plan.Cast.Select(c => c.Name)],
            observations.Count(o => o.Met == true),
            observations.Count(o => o.Met == false),
            problems)
        {
            CharacterNames = created,
        };
    }

    /// <summary>Brings one cast member into the world and sets them up as the plan asked.</summary>
    private async Task<Actor> CastAsync(
        CastMember member,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CharacterPath>(member.Path, ignoreCase: true, out var path))
        {
            var valid = string.Join(", ", Enum.GetNames<CharacterPath>());
            throw new PlaytestException(
                $"'{member.Name}' has path '{member.Path}', which is not one of: {valid}.");
        }

        AccountRole? accountRole = null;

        if (member.Role is { } roleName)
        {
            if (Enum.TryParse<AccountRole>(roleName, ignoreCase: true, out var parsed))
            {
                accountRole = parsed;
            }
            else
            {
                problems.Add($"'{member.Name}' asked for role '{roleName}', which does not exist.");
            }
        }

        // The role is granted inside arriving rather than after it, because the loop is told an
        // actor's role on the EnterWorld message and never looks again.
        return await Actor.ArriveAsync(
            target, transcript, member.Name, path, cancellationToken, accountRole, problems.Add);
    }

    private async Task RunStepsAsync(
        IEnumerable<PlanStep> steps,
        IReadOnlyDictionary<string, Actor> actors,
        Dictionary<string, TimeSpan> since,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            if (step.Together.Count > 0)
            {
                // Simultaneously, so a plan can express a race — two people reaching for the same
                // item, or a duel where both swing. Sequencing these would decide the race in the
                // plan rather than in the game.
                await Task.WhenAll(step.Together.Select(inner =>
                    RunStepAsync(inner, actors, since, problems, cancellationToken)));

                continue;
            }

            await RunStepAsync(step, actors, since, problems, cancellationToken);
        }
    }

    private async Task RunStepAsync(
        PlanStep step,
        IReadOnlyDictionary<string, Actor> actors,
        Dictionary<string, TimeSpan> since,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (step.Note is not null)
        {
            transcript.Add(step.Actor ?? Apparatus, EntryKind.Note, step.Note.Trim());
        }

        if (step.Actor is null)
        {
            return;
        }

        if (!actors.TryGetValue(step.Actor, out var actor))
        {
            problems.Add($"No actor called '{step.Actor}' was in the world when their step ran.");
            return;
        }

        // The window every observation in this step is judged against.
        //
        // <b>It advances only when a step actually says something.</b> A step with no `do` is not a
        // new moment — it is the plan continuing to watch the last thing it did, which is how
        // "walk south" and "then you should be in the tavern" get written as two steps. Advancing
        // on every step broke exactly that: the room description arrived during the walk step's
        // settle, the watching step opened a window after it, and both players sat waiting ten
        // seconds for a line the transcript plainly shows they had already been sent.
        //
        // A step that does say something opens a fresh window, so sequential commands cannot pass
        // on the previous one's output — which is the failure in the other direction, and the one
        // that would quietly make a combat plan meaningless.
        if (step.Do is not null)
        {
            since[step.Actor] = transcript.Now;
        }

        var window = since.GetValueOrDefault(step.Actor, TimeSpan.Zero);

        if (step.Do is not null)
        {
            await actor.SendAsync(Rewrite(step.Do, actors), cancellationToken);
        }

        await WaitAsync(step, actor, window, actors, problems, cancellationToken);
        Observe(step, actor, window, actors);
    }

    /// <summary>
    /// Waits for what the step asked for, recording a timeout rather than raising one.
    /// </summary>
    private async Task WaitAsync(
        PlanStep step,
        Actor actor,
        TimeSpan window,
        IReadOnlyDictionary<string, Actor> actors,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (step.Wait is not { } wait)
        {
            // Nothing asked for, but the answer still arrives asynchronously over the stream, so a
            // step with no wait would judge itself against output that had not arrived yet.
            await Task.Delay(settings.DefaultSettle, cancellationToken);
            return;
        }

        if (wait.Text is { } wanted)
        {
            // Rewritten for the same reason a command is: a plan waiting for "Alice says" must
            // still match when the world had to call her Alicexqbfm.
            var fragment = Rewrite(wanted, actors);

            var timeout = wait.Timeout is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : settings.WaitTimeout;

            var deadline = transcript.Now + timeout;

            while (transcript.Now < deadline)
            {
                if (Seen(actor, window, fragment))
                {
                    break;
                }

                await Task.Delay(settings.PollInterval, cancellationToken);
            }

            if (!Seen(actor, window, fragment))
            {
                // Flagged, not merely noted. The first two-actor plan written against this
                // apparatus had two waits whose text did not match what the game says — "invite"
                // for a line reading "asks you to join", "joined" for one reading "joins" — and
                // the run reported "nothing flagged" while silently spending twenty seconds
                // waiting for words that were never coming. A wait that times out means the plan
                // and the game disagree about what happens, which is the whole subject here,
                // whether the disagreement turns out to be the plan's fault or the game's.
                transcript.Add(actor.Role, EntryKind.Meta,
                    $"waited {timeout.TotalSeconds:0.#}s for \"{wanted}\" and never saw it");

                problems.Add(
                    $"'{actor.Role}' waited {timeout.TotalSeconds:0.#}s for \"{wanted}\" and never saw it.");
            }
        }

        if (wait.Seconds is { } pause)
        {
            await Task.Delay(TimeSpan.FromSeconds(pause), cancellationToken);
        }
    }

    /// <summary>
    /// Records what the step expected against what actually arrived.
    /// </summary>
    /// <remarks>
    /// The observation is recorded under the name the <em>plan</em> used, not the one the world
    /// gave out, because that is what the author wrote and what a reviewer is scanning for. Only
    /// the matching is done against the real name.
    /// </remarks>
    private void Observe(
        PlanStep step,
        Actor actor,
        TimeSpan window,
        IReadOnlyDictionary<string, Actor> actors)
    {
        foreach (var expectation in step.Expectations)
        {
            transcript.AddObservation(
                actor.Role, expectation, Seen(actor, window, Rewrite(expectation, actors)));
        }

        foreach (var prohibition in step.Prohibitions)
        {
            // Recorded as met when the text is absent, so the tick means "as it should be" in both
            // directions and a reviewer scanning for crosses never has to invert one in their head.
            transcript.AddObservation(
                actor.Role,
                $"not: {prohibition}",
                !Seen(actor, window, Rewrite(prohibition, actors)));
        }
    }

    /// <summary>
    /// Whether this actor has seen the text since the step began.
    /// </summary>
    /// <remarks>
    /// Prose and system lines only. A match inside the raw payload of a map frame is not something
    /// a player could have read, and counting it would let an expectation pass on evidence nobody
    /// could see.
    /// </remarks>
    private bool Seen(Actor actor, TimeSpan window, string fragment) =>
        transcript.Since(actor.Role, window).Any(e =>
            e.Kind is EntryKind.Text or EntryKind.Sys or EntryKind.Vitals &&
            e.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Replaces cast names with the names the world actually gave them.
    /// </summary>
    /// <remarks>
    /// A plan writes <c>tell Theron hello</c>, but character names are globally unique, so on the
    /// second run against a server Theron is really Theronqxbfm. Without this every targeted
    /// command in every multi-actor plan would silently miss on any server that had seen the plan
    /// before — and the transcript would show a plausible "You don't see 'Theron' here."
    /// </remarks>
    private static string Rewrite(string command, IReadOnlyDictionary<string, Actor> actors)
    {
        var rewritten = command;

        foreach (var (role, actor) in actors)
        {
            if (!string.Equals(role, actor.CharacterName, StringComparison.Ordinal))
            {
                rewritten = rewritten.Replace(role, actor.CharacterName, StringComparison.OrdinalIgnoreCase);
            }
        }

        return rewritten;
    }

    /// <summary>How the apparatus signs its own lines, as opposed to any actor's.</summary>
    public const string Apparatus = "apparatus";
}

/// <summary>Timings the runner uses. Gathered so a slow server can be given more room.</summary>
public sealed record RunSettings
{
    /// <summary>How long to let the arrival burst land before the first command.</summary>
    public TimeSpan SettleAfterArrival { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a step with no explicit wait gives the answer to arrive.</summary>
    public TimeSpan DefaultSettle { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>How long <c>wait: {text}</c> waits before recording that it never came.</summary>
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How often a wait re-reads the transcript.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);
}
