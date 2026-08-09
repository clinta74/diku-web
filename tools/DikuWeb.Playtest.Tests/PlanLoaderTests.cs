using DikuWeb.Playtest.Plans;
using DikuWeb.Playtest.Session;

namespace DikuWeb.Playtest.Tests;

/// <summary>
/// Reading a plan, and refusing one that would run wrong.
/// </summary>
/// <remarks>
/// Deliberately small. The apparatus's correctness is mostly legible in its own output — a
/// transcript that reads oddly is a transcript somebody notices — and a large test suite for a
/// test tool is where this kind of project goes to die. What is worth testing is the part whose
/// failure is <em>silent</em>: a plan that loads but does not mean what its author wrote.
/// </remarks>
public sealed class PlanLoaderTests
{
    [Fact]
    public void A_plan_reads_its_cast_and_steps()
    {
        var plan = PlanLoader.Parse("""
            name: A fight
            about: Two lines
            cast:
              - name: Theron
                path: Warden
              - name: Vess
                path: Shade
            steps:
              - actor: Theron
                do: kill rat
                wait: { text: "You begin attacking" }
                expect: "You begin attacking"
            """);

        Assert.Equal("A fight", plan.Name);
        Assert.Equal(2, plan.Cast.Count);
        Assert.Equal("Shade", plan.Cast[1].Path);
        Assert.Equal("kill rat", plan.Steps[0].Do);
        Assert.Equal("You begin attacking", plan.Steps[0].Wait?.Text);
    }

    [Fact]
    public void An_expectation_may_be_one_thing_or_several()
    {
        // Both spellings are natural to write, and a format that only took the list form would put
        // brackets round the overwhelmingly common single case.
        var plan = PlanLoader.Parse("""
            name: Shapes
            cast:
              - name: Theron
            steps:
              - actor: Theron
                do: look
                expect: "Exits:"
              - actor: Theron
                do: north
                expect:
                  - "You walk north"
                  - "Exits:"
            """);

        Assert.Equal(["Exits:"], plan.Steps[0].Expectations);
        Assert.Equal(["You walk north", "Exits:"], plan.Steps[1].Expectations);
    }

    [Fact]
    public void A_prohibition_reads_the_same_way()
    {
        var plan = PlanLoader.Parse("""
            name: Leaks
            cast:
              - name: Theron
            steps:
              - actor: Theron
                do: look
                expect-not: "You walk east"
            """);

        Assert.Equal(["You walk east"], plan.Steps[0].Prohibitions);
    }

    [Fact]
    public void A_together_block_keeps_its_own_steps()
    {
        var plan = PlanLoader.Parse("""
            name: Race
            cast:
              - name: Alice
              - name: Bob
            steps:
              - note: Both at once
                together:
                  - actor: Alice
                    do: get coin
                  - actor: Bob
                    do: get coin
            """);

        Assert.Equal(2, plan.Steps[0].Together.Count);
        Assert.Equal("Bob", plan.Steps[0].Together[1].Actor);
    }

    // -----------------------------------------------------------------------
    // The refusals, which are the point
    // -----------------------------------------------------------------------

    [Fact]
    public void A_step_for_somebody_not_in_the_cast_is_refused()
    {
        // The load-bearing one. Such a step simply never runs, and the transcript then shows the
        // game apparently ignoring a command — a plan bug wearing an engine bug's clothes.
        var error = Assert.Throws<PlaytestException>(() => PlanLoader.Parse("""
            name: Typo
            cast:
              - name: Theron
            steps:
              - actor: Theran
                do: look
            """));

        Assert.Contains("Theran", error.Message, StringComparison.Ordinal);
        Assert.Contains("Theron", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_with_no_cast_is_refused()
    {
        var error = Assert.Throws<PlaytestException>(() => PlanLoader.Parse("""
            name: Nobody
            steps:
              - note: Nothing happens
            """));

        Assert.Contains("no cast", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_actors_with_one_name_are_refused()
    {
        // They would share a dictionary entry, so the second would silently replace the first and
        // half the plan would drive a character nobody could see.
        var error = Assert.Throws<PlaytestException>(() => PlanLoader.Parse("""
            name: Twins
            cast:
              - name: Theron
              - name: theron
            steps:
              - actor: Theron
                do: look
            """));

        Assert.Contains("same name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_the_format_does_not_have_is_refused()
    {
        // A silently ignored typo is a plan that quietly stops testing what its author thought.
        var error = Assert.Throws<PlaytestException>(() => PlanLoader.Parse("""
            name: Misspelled
            cast:
              - name: Theron
            steps:
              - actor: Theron
                doo: look
            """));

        Assert.Contains("doo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_plan_is_refused()
    {
        Assert.Throws<PlaytestException>(() => PlanLoader.Parse("# nothing but a comment"));
    }
}
