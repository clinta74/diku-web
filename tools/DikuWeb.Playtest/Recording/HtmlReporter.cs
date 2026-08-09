using System.Globalization;
using System.Net;
using System.Text;
using DikuWeb.Playtest.Running;

namespace DikuWeb.Playtest.Recording;

/// <summary>One plan's record, ready to be written out.</summary>
public sealed record ReportedPlan(PlanOutcome Outcome, Transcript Transcript, string? About);

/// <summary>
/// Writes the run as one self-contained page.
/// </summary>
/// <remarks>
/// <b>The multi-actor view is the whole reason this exists.</b> A single actor's plan reads
/// perfectly well as a text file, and the <c>.log</c> files beside this page are better for that.
/// What no text file does well is two or three people at once: a party fight is only legible if
/// what Bram saw sits beside what Kael saw at the same moment, and the question a reviewer is
/// actually asking — "did the other one see this, and when" — is a question about columns.
///
/// Self-contained by necessity as well as taste: a run directory gets copied around, attached to
/// an issue, and opened from a file:// URL, and anything fetched from a CDN would be missing in
/// every one of those.
/// </remarks>
public static class HtmlReporter
{
    public static string Build(
        DateTimeOffset startedAt,
        string target,
        IReadOnlyList<ReportedPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var page = new StringBuilder();
        var flagged = plans.Count(p => p.Outcome.NeedsReview);

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Playtest ")
            .Append(Escape(startedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))
            .Append("</title><style>")
            .Append(Css)
            .Append("</style></head><body>");

        page.Append("<header class=\"run\"><h1>Playtest run</h1><dl>")
            .Append("<div><dt>When</dt><dd>")
            .Append(Escape(startedAt.ToString("u", CultureInfo.InvariantCulture)))
            .Append("</dd></div><div><dt>Against</dt><dd>")
            .Append(Escape(target))
            .Append("</dd></div><div><dt>Plans</dt><dd>")
            .Append(plans.Count.ToString(CultureInfo.InvariantCulture))
            .Append("</dd></div><div><dt>Flagged</dt><dd class=\"")
            .Append(flagged == 0 ? "ok" : "bad")
            .Append("\">")
            .Append(flagged.ToString(CultureInfo.InvariantCulture))
            .Append("</dd></div></dl>")
            .Append("<p class=\"aside\">A playtest is a recording, not a gate. Nothing here is a "
                  + "failure — the crosses are places the game did not say what the plan expected, "
                  + "which is a question for a person rather than an answer.</p>")
            .Append("<div class=\"toggles\">")
            .Append("<label><input type=\"checkbox\" id=\"hide-vitals\"> Hide vitals</label>")
            .Append("<label><input type=\"checkbox\" id=\"hide-meta\"> Hide apparatus lines</label>")
            .Append("</div></header>");

        foreach (var plan in plans)
        {
            AppendPlan(page, plan);
        }

        page.Append("<script>").Append(Script).Append("</script></body></html>");
        return page.ToString();
    }

    private static void AppendPlan(StringBuilder page, ReportedPlan plan)
    {
        var outcome = plan.Outcome;
        var actors = ActorsIn(plan);

        page.Append("<section class=\"plan\"><h2>")
            .Append(Escape(outcome.Name))
            .Append("</h2>");

        if (!string.IsNullOrWhiteSpace(plan.About))
        {
            page.Append("<p class=\"about\">").Append(Escape(plan.About.Trim())).Append("</p>");
        }

        page.Append("<p class=\"tally\"><span class=\"ok\">")
            .Append(outcome.Met.ToString(CultureInfo.InvariantCulture))
            .Append(" met</span>");

        if (outcome.Unmet > 0)
        {
            page.Append(" <span class=\"bad\">")
                .Append(outcome.Unmet.ToString(CultureInfo.InvariantCulture))
                .Append(" unmet</span>");
        }

        page.Append("</p>");

        foreach (var problem in outcome.Problems)
        {
            page.Append("<p class=\"problem\">").Append(Escape(problem)).Append("</p>");
        }

        // Unmet observations gathered up front, so a reviewer can decide whether to read the whole
        // transcript before reading it. Scanning for crosses in a thousand-line party fight is
        // exactly the chore this page exists to remove.
        var unmet = plan.Transcript.Entries.Where(e => e.Met == false).ToList();

        if (unmet.Count > 0)
        {
            page.Append("<ul class=\"unmet\">");

            foreach (var entry in unmet)
            {
                page.Append("<li><b>").Append(Escape(entry.Actor)).Append("</b> ")
                    .Append(Escape(entry.Text)).Append("</li>");
            }

            page.Append("</ul>");
        }

        AppendTranscript(page, plan, actors);
        page.Append("</section>");
    }

    private static void AppendTranscript(
        StringBuilder page,
        ReportedPlan plan,
        List<string> actors)
    {
        page.Append("<div class=\"grid\" style=\"--actors:")
            .Append(actors.Count.ToString(CultureInfo.InvariantCulture))
            .Append("\">");

        page.Append("<div class=\"head time\">time</div>");

        foreach (var actor in actors)
        {
            page.Append("<div class=\"head\">").Append(Escape(actor)).Append("</div>");
        }

        foreach (var entry in plan.Transcript.Entries)
        {
            if (entry.Kind == EntryKind.Frame)
            {
                continue;
            }

            var column = actors.IndexOf(entry.Actor);

            // A line from the apparatus itself, or from an actor who never made it into the world,
            // spans the whole width rather than being dropped. Losing it would hide exactly the
            // explanation for why the rest of the row is empty.
            var spans = column < 0;

            page.Append("<div class=\"t ")
                .Append(Kind(entry.Kind))
                .Append("\" style=\"grid-column:1\">")
                .Append(Escape(Stamp(entry.Elapsed)))
                .Append("</div>");

            if (spans)
            {
                page.Append("<div class=\"e wide ").Append(Kind(entry.Kind))
                    .Append("\" style=\"grid-column:2/-1\">")
                    .Append(Body(entry))
                    .Append("</div>");

                continue;
            }

            page.Append("<div class=\"e ").Append(Kind(entry.Kind))
                .Append("\" style=\"grid-column:")
                .Append((column + 2).ToString(CultureInfo.InvariantCulture))
                .Append("\">")
                .Append(Body(entry))
                .Append("</div>");
        }

        page.Append("</div>");
    }

    /// <summary>The actors with a column, in the order the plan cast them.</summary>
    private static List<string> ActorsIn(ReportedPlan plan)
    {
        var cast = plan.Outcome.Actors.ToList();

        // Anyone who produced output but was not in the cast list — should not happen, but a
        // reviewer losing lines to a bookkeeping mismatch would be much worse than an extra column.
        foreach (var actor in plan.Transcript.Entries.Select(e => e.Actor).Distinct(StringComparer.Ordinal))
        {
            if (!string.Equals(actor, PlanRunner.Apparatus, StringComparison.Ordinal) &&
                !cast.Contains(actor, StringComparer.Ordinal))
            {
                cast.Add(actor);
            }
        }

        return cast;
    }

    private static string Body(TranscriptEntry entry)
    {
        var text = Escape(entry.Text).Replace("\n", "<br>", StringComparison.Ordinal);

        return entry.Kind switch
        {
            EntryKind.Sent => "<b>&gt; " + text + "</b>",
            EntryKind.Observation => (entry.Met == true ? "✓ " : "✗ ") + text,
            _ => text,
        };
    }

    private static string Kind(EntryKind kind) => kind switch
    {
        EntryKind.Sent => "sent",
        EntryKind.Text => "text",
        EntryKind.Vitals => "vitals",
        EntryKind.Sys => "sys",
        EntryKind.Note => "note",
        EntryKind.Step => "step",
        EntryKind.Observation => "obs",
        EntryKind.Meta => "meta",
        _ => "other",
    };

    private static string Stamp(TimeSpan elapsed) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}");

    private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>
    /// Light and dark, both defined on tokens, because a run directory gets opened by whoever is
    /// asked to look at it and their theme is not knowable from here.
    /// </summary>
    private const string Css = """
        :root {
          --bg: #fbfaf8; --panel: #fff; --ink: #1b1a17; --dim: #6b6862; --rule: #e4e0d9;
          --sent: #1a4f8a; --note: #7a4b12; --ok: #1f6b3a; --bad: #a11b1b; --sys: #5a4a8a;
          --accent: #8a5a1a;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --bg: #14140f; --panel: #1b1b16; --ink: #e8e4da; --dim: #9a958a; --rule: #33322b;
            --sent: #7fb2ea; --note: #d5a45f; --ok: #6fc48c; --bad: #f08c8c; --sys: #b0a0e0;
            --accent: #d5a45f;
          }
        }
        * { box-sizing: border-box; }
        body {
          margin: 0; padding: 1.5rem; background: var(--bg); color: var(--ink);
          font: 14px/1.5 ui-monospace, "Cascadia Code", Menlo, Consolas, monospace;
        }
        h1 { font-size: 1.3rem; margin: 0 0 .75rem; }
        h2 { font-size: 1.1rem; margin: 0 0 .5rem; color: var(--accent); }
        header.run, section.plan {
          background: var(--panel); border: 1px solid var(--rule); border-radius: 8px;
          padding: 1rem 1.25rem; margin-bottom: 1.25rem; max-width: 100%;
        }
        dl { display: flex; flex-wrap: wrap; gap: 1.5rem; margin: 0 0 .75rem; }
        dl div { display: flex; gap: .5rem; }
        dt { color: var(--dim); }
        dd { margin: 0; }
        .aside, .about { color: var(--dim); white-space: pre-wrap; margin: .5rem 0; }
        .toggles { display: flex; gap: 1rem; color: var(--dim); }
        .toggles label { cursor: pointer; }
        .ok { color: var(--ok); }
        .bad { color: var(--bad); font-weight: 600; }
        .tally { margin: .25rem 0 .5rem; }
        .problem { color: var(--bad); margin: .25rem 0; }
        .unmet { margin: .5rem 0 1rem; padding-left: 1.25rem; color: var(--bad); }
        .grid {
          display: grid;
          grid-template-columns: 5.5rem repeat(var(--actors), minmax(16rem, 1fr));
          gap: 0 1rem; overflow-x: auto; border-top: 1px solid var(--rule); padding-top: .5rem;
        }
        .head {
          position: sticky; top: 0; background: var(--panel); color: var(--accent);
          font-weight: 600; padding: .25rem 0; border-bottom: 1px solid var(--rule); z-index: 1;
        }
        .head.time { grid-column: 1; }
        .t { color: var(--dim); font-size: .85em; white-space: nowrap; }
        .e { white-space: pre-wrap; overflow-wrap: anywhere; }
        .e.wide { color: var(--dim); }
        .sent { color: var(--sent); }
        .note { color: var(--note); font-weight: 600; padding: .5rem 0; }
        .sys { color: var(--sys); }
        .vitals, .meta { color: var(--dim); font-size: .9em; }
        .obs { font-weight: 600; }
        .obs:has(+ *) { }
        body.no-vitals .vitals, body.no-vitals .vitals + .e { display: none; }
        body.no-meta .meta { display: none; }
        """;

    /// <summary>
    /// Hiding a line has to hide its timestamp too, or the grid grows a column of orphaned times.
    /// Done in script rather than CSS because the timestamp is a sibling cell, not a child.
    /// </summary>
    private const string Script = """
        for (const [id, cls] of [['hide-vitals', 'vitals'], ['hide-meta', 'meta']]) {
          document.getElementById(id).addEventListener('change', e => {
            for (const cell of document.querySelectorAll('.e.' + cls)) {
              const time = cell.previousElementSibling;
              cell.style.display = e.target.checked ? 'none' : '';
              if (time && time.classList.contains('t')) {
                time.style.display = e.target.checked ? 'none' : '';
              }
            }
          });
        }
        """;
}
