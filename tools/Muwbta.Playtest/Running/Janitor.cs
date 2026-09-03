using Muwbta.Domain.Accounts;
using Muwbta.Domain.Characters;
using Muwbta.Playtest.Recording;
using Muwbta.Playtest.Session;
using Muwbta.Playtest.Targets;

namespace Muwbta.Playtest.Running;

/// <summary>
/// Deletes the characters a run created, once the run is over.
/// </summary>
/// <remarks>
/// Every actor registers a real account and a real character against the real database, and there
/// is no delete endpoint — deliberately. Left alone, a long-lived dev server accumulates one
/// character per actor per run for ever: fifty-four of them turned up in `who` within a day of the
/// apparatus existing.
///
/// <b>One janitor per run, not per plan</b>, and it goes in last. It cannot delete the character
/// it is playing — the verb refuses that, correctly — so a janitor per plan would leave one behind
/// each time and simply reduce the rate. One per run leaves exactly one.
///
/// It only ever names characters <em>this run</em> created, by the name the world actually gave
/// them. Deleting by the plan's name would delete whoever happened to be called Theron.
/// </remarks>
public static class Janitor
{
    /// <summary>How long to let the world answer each deletion before moving on.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Retires <paramref name="characters"/>, or explains why it could not.
    /// </summary>
    /// <returns>A line for the console, whatever happened.</returns>
    public static async Task<string> SweepAsync(
        IGameTarget target,
        Transcript transcript,
        IReadOnlyCollection<string> characters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(characters);

        if (characters.Count == 0)
        {
            return "Nothing to clean up.";
        }

        var problems = new List<string>();

        Actor? janitor = null;

        try
        {
            janitor = await Actor.ArriveAsync(
                target,
                transcript,
                "Janitor",
                CharacterPath.Warden,
                cancellationToken,
                AccountRole.Admin,
                problems.Add);

            if (problems.Count > 0)
            {
                // No admin credential, which is the ordinary case rather than a failure. Say what
                // was left behind so it can be purged by hand.
                return $"Left {characters.Count} character(s) behind: {problems[0]}";
            }

            foreach (var character in characters)
            {
                await janitor.SendAsync($"deletecharacter {character}", cancellationToken);
                await Task.Delay(Settle, cancellationToken);
            }

            // Hand the privilege back. Ten runs left ten Admin accounts standing, all with the
            // same known password, because deleting the character never touched the account — a
            // dev database only, but "the test tool quietly manufactures administrators" is not a
            // sentence that should be true anywhere. Borrowed authority gets returned.
            var demoted = await target.PromoteAsync(
                janitor.Username, AccountRole.Player, cancellationToken);

            // The janitor's own character cannot be deleted by the janitor, so it is the one thing
            // every run leaves. Named in the summary rather than hidden, because a row that
            // appears from nowhere is worse than one that was announced.
            var remains = $"Deleted {characters.Count} character(s). " +
                          $"{janitor.CharacterName} remains — a janitor cannot delete itself.";

            return demoted.Granted
                ? remains
                : $"{remains} WARNING: it is still an Admin — {demoted.Reason}";
        }
        catch (PlaytestException ex)
        {
            return $"Could not clean up: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            return "Clean-up was interrupted.";
        }
        finally
        {
            if (janitor is not null)
            {
                await janitor.DisposeAsync();
            }
        }
    }
}
