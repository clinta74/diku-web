using System.Globalization;
using System.Text;
using Muwbta.Persistence;
using Muwbta.Server.Building;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Assist;

/// <summary>
/// What the model is told about a mob, item, or quest before it writes a word about it.
/// </summary>
/// <param name="Facts">
/// The mechanical truth, rendered as a few lines. This is the half of the design that makes
/// generated prose worth having: the numbers are already decided, and a description written
/// without seeing them is a description that will contradict them. It is also why those fields are
/// <em>input</em> rather than output - see <c>AssistSchema.MobNotGenerated</c>.
/// </param>
/// <param name="Exemplars">
/// A few existing descriptions of the same kind, for voice. Extracted rather than whole files: a
/// zone bundle is ~36,000 tokens against a 16,384 window, so showing it the content is not
/// available at any price.
/// </param>
public sealed record ProseContext(string Facts, IReadOnlyList<string> Exemplars);

/// <summary>Assembles <see cref="ProseContext"/> for one entity.</summary>
public interface IProseContextSource
{
    Task<ProseContext?> ForAsync(
        AssistSchema.ProseKind kind, string key, CancellationToken cancellationToken);
}

/// <summary>Reads the template or quest, and a few of its neighbours, out of Postgres.</summary>
public sealed class EfProseContextSource(MuwbtaDbContext db) : IProseContextSource
{
    private const int Exemplars = 3;

    /// <summary>Below this a description is a placeholder rather than an example.</summary>
    private const int WorthLearningFrom = 80;

    public async Task<ProseContext?> ForAsync(
        AssistSchema.ProseKind kind, string key, CancellationToken cancellationToken) => kind switch
        {
            AssistSchema.ProseKind.Mob => await MobAsync(key, cancellationToken).ConfigureAwait(false),
            AssistSchema.ProseKind.Item => await ItemAsync(key, cancellationToken).ConfigureAwait(false),
            _ => await QuestAsync(key, cancellationToken).ConfigureAwait(false),
        };

    private async Task<ProseContext?> MobAsync(string key, CancellationToken cancellationToken)
    {
        var mob = await db.MobTemplates.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Key == key, cancellationToken).ConfigureAwait(false);

        if (mob is null)
        {
            return null;
        }

        var facts = new StringBuilder()
            .Append("Level ").Append(mob.Level).Append(".\n")
            .Append("Gives ").Append(mob.BaseXp).Append(" xp and ").Append(mob.BaseGold)
            .Append(" gold.\n");

        Bag(facts, "Stats", mob.BaseStats);

        if (mob.Attacks.Count > 0)
        {
            // How a thing fights is most of what a player sees of it, so it is the fact most worth
            // having in the prose - "it lunges" reads differently from "it swings".
            facts.Append("Attacks: ")
                .Append(string.Join(", ", mob.Attacks.Select(a => a.Verb).Where(v => !string.IsNullOrWhiteSpace(v))))
                .Append('\n');
        }

        var neighbours = await db.MobTemplates.AsNoTracking()
            .Where(m => m.Key != key && m.Description.Length >= WorthLearningFrom)
            .OrderBy(m => Math.Abs(m.Level - mob.Level))
            .Take(Exemplars)
            .Select(m => m.Description)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProseContext(facts.ToString(), neighbours);
    }

    private async Task<ProseContext?> ItemAsync(string key, CancellationToken cancellationToken)
    {
        var item = await db.ItemTemplates.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Key == key, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return null;
        }

        var facts = new StringBuilder();

        facts.Append("Worn on: ")
            .Append(item.Slots.Count == 0 ? "nothing; it is carried" : string.Join(", ", item.Slots))
            .Append(item.IsTwoHanded ? " (two-handed)" : string.Empty)
            .Append('\n');

        // Grams, because that is the unit the column is in, but said in kilograms because nobody
        // thinks about a sword in grams and the model is being asked to write like a person.
        facts.Append("Weight: ")
            .Append((item.Weight / 1000.0).ToString("0.#", CultureInfo.InvariantCulture))
            .Append(" kg. Worth ").Append(item.BaseValue).Append(" gold.\n");

        Bag(facts, "Stats", item.BaseStats);

        if (!string.IsNullOrWhiteSpace(item.AttackVerb))
        {
            facts.Append("Used to ").Append(item.AttackVerb).Append(".\n");
        }

        foreach (var (label, on) in new[]
        {
            ("It is a quest item.", item.IsQuestItem),
            ("It gives light.", item.IsLightSource),
            ("It cannot be dropped.", item.IsNoDrop),
            ("It is food.", item.FoodValue > 0),
            ("It is a drink.", item.DrinkValue > 0),
        })
        {
            if (on)
            {
                facts.Append(label).Append('\n');
            }
        }

        var neighbours = await db.ItemTemplates.AsNoTracking()
            .Where(i => i.Key != key && i.Description.Length >= WorthLearningFrom)
            .OrderByDescending(i => i.Description.Length)
            .Take(Exemplars)
            .Select(i => i.Description)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProseContext(facts.ToString(), neighbours);
    }

    private async Task<ProseContext?> QuestAsync(string key, CancellationToken cancellationToken)
    {
        var quest = await db.Quests.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Key == key, cancellationToken).ConfigureAwait(false);

        if (quest is null)
        {
            return null;
        }

        // Resolved to names rather than passed as keys. `ossara.gatetown.toll-clerk` tells the
        // model nothing it can write with; "the toll clerk" does, and the key would only invite it
        // to put a key in the prose.
        var mobs = await db.MobTemplates.AsNoTracking()
            .Where(m => m.Key == quest.GiverMobKey || m.Key == quest.TurninMobKey)
            .ToDictionaryAsync(m => m.Key, m => m.Name, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var facts = new StringBuilder()
            .Append("Given by: ").Append(Named(mobs, quest.GiverMobKey)).Append('\n')
            .Append("Handed in to: ").Append(Named(mobs, quest.TurninMobKey)).Append('\n');

        if (!string.IsNullOrWhiteSpace(quest.RequiredItemKey))
        {
            var required = await db.ItemTemplates.AsNoTracking()
                .Where(i => i.Key == quest.RequiredItemKey)
                .Select(i => i.Name)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            facts.Append("Asks for: ").Append(quest.RequiredCount).Append(" x ")
                .Append(required ?? quest.RequiredItemKey).Append('\n');
        }

        facts.Append("Pays: ").Append(quest.RewardXp).Append(" xp, ")
            .Append(quest.RewardGold).Append(" gold\n");

        var neighbours = await db.Quests.AsNoTracking()
            .Where(q => q.Key != key && q.Description.Length >= WorthLearningFrom)
            .Take(Exemplars)
            .Select(q => q.Description)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProseContext(facts.ToString(), neighbours);
    }

    private static string Named(IReadOnlyDictionary<string, string> mobs, string? key) =>
        key is null ? "nobody" : mobs.GetValueOrDefault(key, key);

    /// <summary>
    /// A stat bag, rendered only when it has something in it.
    /// </summary>
    /// <remarks>
    /// The keys are the combat vocabulary (<c>EquipmentResolver.KnownStatKeys</c>) and they read
    /// well enough as-is; naming them prettily here would be a second vocabulary to keep in step
    /// with the first for no gain the model can use.
    /// </remarks>
    private static void Bag(StringBuilder facts, string label, Dictionary<string, object> bag)
    {
        if (bag.Count == 0)
        {
            return;
        }

        facts.Append(label).Append(": ")
            .Append(string.Join(", ", bag.Select(p => $"{p.Key} {p.Value}")))
            .Append('\n');
    }
}
