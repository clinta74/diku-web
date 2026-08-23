using DikuWeb.Server.Assist;
using DikuWeb.Server.Building;

namespace DikuWeb.Server.Tests.Assist;

/// <summary>
/// The prose schemas cover their records, and cover only prose.
/// </summary>
public sealed class ProseSchemaTests
{
    /// <summary>
    /// Every field of every record is either generated or explained.
    /// </summary>
    /// <remarks>
    /// The same guarantee <c>AssistSchemaTests</c> gives <c>BundleRoom</c>, extended to the three
    /// kinds that followed it. A field added to a template and forgotten here is a field the assist
    /// silently never fills in, which reads from the builder's side as the model being bad at its
    /// job rather than as nobody having decided.
    /// </remarks>
    [Theory]
    [InlineData(AssistSchema.ProseKind.Mob, typeof(BundleMobTemplate))]
    [InlineData(AssistSchema.ProseKind.Item, typeof(BundleItemTemplate))]
    [InlineData(AssistSchema.ProseKind.Quest, typeof(BundleQuest))]
    public void Every_field_is_generated_or_explained(AssistSchema.ProseKind kind, Type record)
    {
        var excluded = Excluded(kind);

        var generated = AssistSchema.ForProse(kind)["properties"]!.AsObject()
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undecided = record.GetProperties()
            .Select(p => p.Name)
            .Where(name => !generated.Contains(name) && !excluded.ContainsKey(name))
            .ToList();

        Assert.Empty(undecided);
    }

    /// <summary>And nothing is excluded that no longer exists.</summary>
    [Theory]
    [InlineData(AssistSchema.ProseKind.Mob, typeof(BundleMobTemplate))]
    [InlineData(AssistSchema.ProseKind.Item, typeof(BundleItemTemplate))]
    [InlineData(AssistSchema.ProseKind.Quest, typeof(BundleQuest))]
    public void Nothing_is_excluded_that_the_record_does_not_have(
        AssistSchema.ProseKind kind, Type record)
    {
        var fields = record.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Excluded(kind).Keys, k => !fields.Contains(k));
    }

    /// <summary>
    /// Nothing mechanical is generated, for any kind.
    /// </summary>
    /// <remarks>
    /// The <c>respawn: true</c> lesson, stated as a rule rather than trusted to have been applied.
    /// A model handed a level, a weight or a reward will fill it in - it cannot decline - and every
    /// one of those decides whether content is survivable, affordable or worth doing.
    /// </remarks>
    [Theory]
    [InlineData(AssistSchema.ProseKind.Mob)]
    [InlineData(AssistSchema.ProseKind.Item)]
    [InlineData(AssistSchema.ProseKind.Quest)]
    public void Only_words_are_generated(AssistSchema.ProseKind kind)
    {
        var generated = AssistSchema.ForProse(kind)["properties"]!.AsObject()
            .Select(p => p.Key)
            .ToList();

        Assert.All(generated, key => Assert.Contains(key, new[] { "name", "description", "summary" }));
    }

    /// <summary>A quest has a summary; the other two do not.</summary>
    [Fact]
    public void Only_a_quest_is_asked_for_a_summary()
    {
        Assert.True(Properties(AssistSchema.ProseKind.Quest).ContainsKey("summary"));
        Assert.False(Properties(AssistSchema.ProseKind.Mob).ContainsKey("summary"));
        Assert.False(Properties(AssistSchema.ProseKind.Item).ContainsKey("summary"));
    }

    /// <summary>The two keywords a grammar rests on, on every kind.</summary>
    [Theory]
    [InlineData(AssistSchema.ProseKind.Mob)]
    [InlineData(AssistSchema.ProseKind.Item)]
    [InlineData(AssistSchema.ProseKind.Quest)]
    public void Every_prose_schema_is_closed_and_says_what_it_requires(AssistSchema.ProseKind kind)
    {
        var schema = AssistSchema.ForProse(kind);

        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        var required = schema["required"]!.AsArray().Select(v => v!.GetValue<string>()).ToList();

        Assert.Contains("name", required);
        Assert.Contains("description", required);
    }

    /// <summary>Each kind is told what it is writing, rather than being given one generic brief.</summary>
    [Fact]
    public void Each_kind_gets_its_own_words()
    {
        var mob = Properties(AssistSchema.ProseKind.Mob)["name"]!["description"]!.GetValue<string>();
        var item = Properties(AssistSchema.ProseKind.Item)["name"]!["description"]!.GetValue<string>();

        Assert.NotEqual(mob, item);
        Assert.Contains("creature", mob, StringComparison.Ordinal);
        Assert.Contains("inventory", item, StringComparison.Ordinal);
    }

    private static System.Text.Json.Nodes.JsonObject Properties(AssistSchema.ProseKind kind) =>
        AssistSchema.ForProse(kind)["properties"]!.AsObject();

    private static IReadOnlyDictionary<string, string> Excluded(AssistSchema.ProseKind kind) => kind switch
    {
        AssistSchema.ProseKind.Mob => AssistSchema.MobNotGenerated,
        AssistSchema.ProseKind.Item => AssistSchema.ItemNotGenerated,
        _ => AssistSchema.QuestNotGenerated,
    };
}

/// <summary>
/// What the prose review catches.
/// </summary>
public sealed class ProseDraftReviewTests
{
    private static ProseDraft Draft(string description, string? summary = null) =>
        new("a rim-wolf", description, summary);

    [Fact]
    public void A_good_draft_has_nothing_to_say()
    {
        Assert.Empty(RoomDraftReviewProse(Draft("Lean, and watching the treeline.")));
    }

    /// <summary>
    /// A content key in the prose.
    /// </summary>
    /// <remarks>
    /// The facts handed to the model resolve keys to names precisely so this does not happen, but
    /// a quest prompt still carries counts and rewards and a model that has seen a dotted key will
    /// occasionally put one in a sentence. A player seeing <c>ossara.gatetown.toll-clerk</c> in a
    /// description is looking at a bug.
    /// </remarks>
    [Fact]
    public void A_content_key_in_the_prose_is_a_warning()
    {
        var warnings = RoomDraftReviewProse(
            Draft("Take this to ossara.gatetown.toll-clerk before dusk."));

        Assert.Contains(warnings, w => w.Contains("content key", StringComparison.Ordinal));
    }

    /// <summary>An ordinary sentence ending is not a key.</summary>
    [Fact]
    public void A_full_stop_is_not_a_key()
    {
        Assert.Empty(RoomDraftReviewProse(Draft("Lean. Watching. Patient.")));
    }

    [Fact]
    public void A_quest_without_a_summary_is_a_warning()
    {
        var warnings = ProseDraftReview.Review(
            new ProseDraft("The Toll", "Pay what is owed.", null),
            AssistSchema.ProseKind.Quest);

        Assert.Contains(warnings, w => w.Contains("summary", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mob_with_a_summary_is_a_warning()
    {
        var warnings = ProseDraftReview.Review(
            new ProseDraft("a rim-wolf", "Lean and watchful.", "Kill the wolf"),
            AssistSchema.ProseKind.Mob);

        Assert.Contains(warnings, w => w.Contains("only a quest", StringComparison.Ordinal));
    }

    /// <summary>
    /// A draft that opens with an exemplar's own sentence.
    /// </summary>
    /// <remarks>
    /// Not hypothetical. The first live run of the prose path copied the exemplar's first sentence
    /// verbatim into both drafts, and in the item's case dragged the description away from the
    /// facts with it - an exemplar about a short blade turned a 6.4 kg two-handed axe into one. The
    /// prompt now says not to; this notices when it does anyway, because the result reads perfectly
    /// well and only looks wrong beside the description it was taken from.
    /// </remarks>
    [Fact]
    public void Opening_with_a_copied_sentence_is_a_warning()
    {
        var exemplar = "It keeps to the walls, and watches the door more than it watches you.";

        var warnings = ProseDraftReview.Review(
            new ProseDraft("a rim-wolf", exemplar + " Its ribs show beneath matted fur.", null),
            AssistSchema.ProseKind.Mob,
            [exemplar]);

        Assert.Contains(warnings, w => w.Contains("copied", StringComparison.Ordinal));
    }

    /// <summary>Writing in the same voice is the point, and is not copying.</summary>
    [Fact]
    public void Sounding_like_the_exemplars_is_not_copying()
    {
        var warnings = ProseDraftReview.Review(
            new ProseDraft("a rim-wolf", "It keeps to the treeline, and will not come closer.", null),
            AssistSchema.ProseKind.Mob,
            ["It keeps to the walls, and watches the door more than it watches you."]);

        Assert.DoesNotContain(warnings, w => w.Contains("copied", StringComparison.Ordinal));
    }

    /// <summary>With no exemplars to compare against, the check simply does not run.</summary>
    [Fact]
    public void No_exemplars_means_no_copy_check()
    {
        Assert.Empty(RoomDraftReviewProse(Draft("Anything at all.")));
    }

    private static IReadOnlyList<string> RoomDraftReviewProse(ProseDraft draft) =>
        ProseDraftReview.Review(draft, AssistSchema.ProseKind.Mob);
}
