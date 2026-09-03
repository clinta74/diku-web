using Muwbta.Domain.Quests;
using Muwbta.Engine.Quests;
using Muwbta.Server.Building;

namespace Muwbta.Server.Tests.Building;

/// <summary>
/// The authored offers carry the link a player clicks (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// Marking the words is optional to the engine — an unmarked offer falls back to a dim line naming
/// the command — which is what let 35 quests keep working the day the marker landed. That
/// tolerance is exactly why this file exists: an offer that quietly lost its marker would still
/// work, still read fine, and be discoverable only by playing that quest.
/// </para>
/// <para>
/// It reads the content off disk rather than a fixture, for the reason the weapon balance tests
/// do: what needs asserting is the shipped prose, and a fixture would agree with itself.
/// </para>
/// </remarks>
public sealed class QuestOfferContentTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Muwbta.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static WorldBundle World()
    {
        var sources = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "content"), "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(path =>
            {
                Assert.True(BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var error), error);
                return new BundleSource(path, bundle!);
            })
            .ToList();

        var merged = BundleMerge.Merge(sources);
        Assert.True(merged.Ok, string.Join("\n", merged.Errors));
        return merged.Bundle!;
    }

    private static string Offer(BundleQuest quest) =>
        quest.Dialogue.TryGetValue(QuestDialogue.GiverOffer, out var offer) ? offer : string.Empty;

    [Fact]
    public void Every_quest_can_be_taken_on_by_clicking_its_own_prose()
    {
        var unmarked = World().Quests
            .Where(q => QuestOffer.Keywords(Offer(q)).Count == 0)
            .Select(q => q.Key)
            .ToList();

        Assert.Empty(unmarked);
    }

    /// <summary>
    /// One link per offer. Two would both work, and would read as two errands in one sentence.
    /// </summary>
    [Fact]
    public void No_offer_marks_more_than_one_thing()
    {
        var crowded = World().Quests
            .Where(q => QuestOffer.Keywords(Offer(q)).Count > 1)
            .Select(q => q.Key)
            .ToList();

        Assert.Empty(crowded);
    }

    /// <summary>
    /// The marked words are in the sentence, not appended to it.
    /// </summary>
    /// <remarks>
    /// The whole point of authoring the link rather than generating it is that it sits on the noun
    /// the errand is about. A marker wrapping the entire line would be a parenthetical with extra
    /// steps, and a marker at the very start usually means the prose was written around the link
    /// instead of the other way round.
    /// </remarks>
    [Fact]
    public void The_link_sits_inside_the_sentence()
    {
        foreach (var quest in World().Quests)
        {
            var offer = Offer(quest);
            var keyword = Assert.Single(QuestOffer.Keywords(offer));

            Assert.True(
                keyword.Length < QuestOffer.Plain(offer).Length / 2,
                $"{quest.Key} marks '{keyword}', which is most of the line");
        }
    }

    /// <summary>
    /// And the shipped content passes every rule the import enforces, this one included.
    /// </summary>
    /// <remarks>
    /// Which is what makes the collision rule meaningful rather than theoretical: no giver in the
    /// Reaches can offer two quests at once today — every multi-quest giver is a chain or a Path
    /// fan-out — so this is the assertion that would notice the day one can.
    /// </remarks>
    [Fact]
    public void The_authored_world_imports_without_errors()
    {
        var findings = BundleValidator.Validate(World());

        Assert.True(
            findings.Ok,
            string.Join("\n", findings.Findings
                .Where(f => f.Level == BundleFindingLevel.Error)
                .Select(f => f.Message)));
    }
}
