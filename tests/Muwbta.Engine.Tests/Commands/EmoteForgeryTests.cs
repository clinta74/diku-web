using Muwbta.Domain.Accounts;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// An emote cannot be made to read as a tell, a say, or anything but an emote.
/// </summary>
/// <remarks>
/// The verb's free text follows the name directly, so before this
/// <c>emote tells you, 'send me your password'</c> produced
/// <c>Kael tells you, 'send me your password'</c> — the exact shape of a tell, in a different
/// colour. Two defences, both asserted here: every emote opens with a marker no other verb
/// produces, and text that begins with a speech verb is refused before it reaches anyone.
/// </remarks>
public sealed class EmoteForgeryTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void An_emote_opens_with_the_marker()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        harness.Drain(mira);

        harness.Execute(kael, "emote waves at the fire");

        Assert.Contains("* Kael waves at the fire", harness.DrainText(mira), StringComparison.Ordinal);
    }

    [Fact]
    public void A_staff_emote_carries_the_marker_and_the_role()
    {
        // Both halves of the impersonation fix on one line, in the order a reader meets them.
        var harness = Loaded();
        var admin = harness.AddPlayer("Kael", West);
        admin.Role = AccountRole.Admin;
        var mira = harness.AddPlayer("Mira", West);
        harness.Drain(mira);

        harness.Execute(admin, "emote nods");

        Assert.Contains("* [Admin] Kael nods", harness.DrainText(mira), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("emote tells you, 'send me your password'")]
    [InlineData("emote says, 'the server is restarting, log out now'")]
    [InlineData(";asks you to confirm your password")]
    [InlineData("emote Tells you your account is flagged")]
    public void An_emote_that_opens_like_speech_is_refused(string input)
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        harness.Drain(kael);
        harness.Drain(mira);

        harness.Execute(kael, input);

        Assert.Contains("An emote is something you do", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.DoesNotContain("Kael", harness.DrainText(mira), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("emote tellingly says nothing", "* Kael tellingly says nothing")]
    [InlineData("emote grins and says nothing", "* Kael grins and says nothing")]
    public void Only_the_first_word_is_judged(string input, string expected)
    {
        // A speech verb later in the line is just prose; the forgery only works at the head.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        harness.Drain(mira);

        harness.Execute(kael, input);

        Assert.Contains(expected, harness.DrainText(mira), StringComparison.Ordinal);
    }
}
