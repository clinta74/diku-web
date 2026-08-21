using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Items;
using DikuWeb.Server.Building;

namespace DikuWeb.Balance.Content;

/// <summary>
/// What a character of a given Path is wearing when the fight starts.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the harness's largest assumption and it is deliberately the most visible one.</b>
/// Nothing in the content says what a level 30 Warden owns — items carry no level requirement, and
/// the only thing tying gear to progression is which realm authored it. So the model is: <em>a
/// character fighting in a realm is wearing that realm's best, for their Path.</em> That is the
/// optimistic end of the range, and it is the right end to measure from: if abilities are
/// negligible next to a best-in-realm weapon, they are more negligible next to a worse one, and
/// the finding survives the assumption being generous.
/// </para>
/// <para>
/// Every chosen piece is printed with the result, so a reader who disagrees with a pick can see it
/// rather than infer it from a damage number.
/// </para>
/// </remarks>
public sealed record Loadout(
    string Realm,
    GearTier Tier,
    IReadOnlyList<ItemInstance> Equipped)
{
    /// <summary>Nothing equipped: bare fists and no armour.</summary>
    public static Loadout Naked { get; } = new("(none)", GearTier.Standard, []);

    /// <summary>
    /// What this realm offers a character of this Path, at the given tier, one item per slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="GearTier.Standard"/> is the baseline, and the epic line is deliberately
    /// excluded from it.</b> An epic is a reward for clearing the realm; it cannot also be the
    /// equipment you are assumed to hold while clearing it, or the content is gated behind itself.
    /// The question the harness exists to answer — can a correctly equipped solo player get through
    /// a zone's ordinary mobs — is a question about shop and drop gear, so that is what
    /// <see cref="GearTier.Standard"/> means.
    /// </para>
    /// <para>
    /// <see cref="GearTier.Epic"/> then measures what the reward is worth, by changing exactly one
    /// thing. Where the two disagree about whether a fight is winnable at all, the epic has stopped
    /// being a bonus and become a requirement, and that is a finding rather than a tuning detail.
    /// </para>
    /// <para>
    /// Armour is unchanged between the tiers. The epic line is weapons only, so folding armour into
    /// the distinction would make the comparison measure two things at once.
    /// </para>
    /// </remarks>
    public static Loadout For(ContentSet content, CharacterPath path, string realm, GearTier tier)
    {
        ArgumentNullException.ThrowIfNull(content);

        var candidates = content.Items
            .Where(i => RealmOf(content, i.Key) == realm)
            .Where(i => Allows(i, path))
            .Where(i => tier == GearTier.Epic || !IsEpic(i))
            .ToList();

        var equipped = new List<ItemInstance>();

        // At the epic tier the Path's own epic is preferred outright rather than ranked, because it
        // is the author saying what this Path should be holding here. Ranking by damage per second
        // instead handed a level 50 Adept a two-handed hammer that beat `epic-adept-5` by three
        // percent on paper and cost them the shield.
        var mainHand = candidates
            .Where(i => Fits(i, ItemSlot.MainHand) && i.AttackDelayPulses is not null)
            .OrderByDescending(i => tier == GearTier.Epic && IsEpicFor(i, path) ? 1 : 0)
            .ThenByDescending(WeaponScore)
            .FirstOrDefault();

        if (mainHand is not null)
        {
            equipped.Add(Instance(mainHand, ItemSlot.MainHand));
        }

        // A two-handed main hand occupies both, so the off hand is only filled when the main hand
        // leaves it free. Getting this wrong would hand a greatsword user a second greatsword.
        if (mainHand is { IsTwoHanded: false })
        {
            var offHand = candidates
                .Where(i => Fits(i, ItemSlot.OffHand) && !i.IsTwoHanded)
                .OrderByDescending(i => i.AttackDelayPulses is null ? ArmorScore(i) : WeaponScore(i))
                .FirstOrDefault();

            if (offHand is not null)
            {
                equipped.Add(Instance(offHand, ItemSlot.OffHand));
            }
        }

        foreach (var slot in ArmorSlots)
        {
            var piece = candidates
                .Where(i => Fits(i, slot))
                .OrderByDescending(ArmorScore)
                .FirstOrDefault();

            if (piece is not null)
            {
                equipped.Add(Instance(piece, slot));
            }
        }

        return new Loadout(realm, tier, equipped);
    }

    /// <summary>Whether this item is part of the epic reward line at all, for any Path.</summary>
    private static bool IsEpic(BundleItemTemplate item) =>
        item.Key.StartsWith("epic-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is the epic reward line's entry for this Path.</summary>
    private static bool IsEpicFor(BundleItemTemplate item, CharacterPath path) =>
        item.Key.StartsWith(
            $"epic-{path.ToString().ToLowerInvariant()}-", StringComparison.OrdinalIgnoreCase);

    /// <summary>The realm an item belongs to, or <see cref="RealmIndex.Unplaced"/>.</summary>
    private static string RealmOf(ContentSet content, string key) =>
        content.ItemRealms.TryGetValue(key, out var realm) ? realm : RealmIndex.Unplaced;

    private static readonly ItemSlot[] ArmorSlots =
        [ItemSlot.Head, ItemSlot.Chest, ItemSlot.Hands, ItemSlot.Legs, ItemSlot.Feet, ItemSlot.Trinket];

    /// <summary>
    /// A weapon's damage per second, which is the only ranking that survives weapons having
    /// different speeds.
    /// </summary>
    /// <remarks>
    /// Ranking by dice alone is how <c>epic-warden-2</c>, <c>-4</c> and <c>-5</c> came to be
    /// strictly worse than the Hallow line at three of five tiers — identical dice, slower swing,
    /// and nothing in the numbers said so (see <c>WeaponBalanceTests</c>). The harness must not
    /// repeat the mistake it exists to measure.
    /// </remarks>
    private static double WeaponScore(BundleItemTemplate item)
    {
        var min = Stat(item, "damageMin", 1);
        var max = Stat(item, "damageMax", 2);
        var delay = AttackTiming.Clamp(item.AttackDelayPulses);

        return (min + max) / 2.0 / (delay / 4.0);
    }

    /// <summary>
    /// A piece of armour's worth, in armour-rating equivalents.
    /// </summary>
    /// <remarks>
    /// <c>armor</c> and <c>defense</c> do different jobs — what a blow costs, and how often one
    /// lands (PLAN.md §4.6) — so they cannot simply be added. A defence point moves the needed roll
    /// by one face of twenty, which is five percent of incoming damage; near
    /// <c>ArmorCurve.Midpoint</c> it takes roughly twenty armour to buy the same five percent. That
    /// is where the 20 comes from. It only has to rank pieces within one realm, not price them.
    /// </remarks>
    private static double ArmorScore(BundleItemTemplate item) =>
        Stat(item, "armor", 0) + (20 * Stat(item, "defense", 0));

    private static bool Fits(BundleItemTemplate item, ItemSlot slot) =>
        item.Slots is { Count: > 0 } slots && slots.Contains(slot);

    /// <summary>An empty or absent Path list means anyone may wear it.</summary>
    private static bool Allows(BundleItemTemplate item, CharacterPath path) =>
        item.Paths is not { Count: > 0 } paths || paths.Contains(path);

    private static double Stat(BundleItemTemplate item, string key, double fallback) =>
        item.BaseStats is not null && StatReader.TryReadDecimal(item.BaseStats, key, out var value)
            ? (double)value
            : fallback;

    /// <summary>
    /// An <see cref="ItemInstance"/> carrying the template's stats verbatim, which is what
    /// <c>ItemSpawner</c> produces: a zone's <c>itemValue</c> dial moves the price and never the
    /// numbers.
    /// </summary>
    private static ItemInstance Instance(BundleItemTemplate template, ItemSlot slot) => new()
    {
        TemplateKey = template.Key,
        TemplateName = template.Name,
        ResolvedStats = new Dictionary<string, object>(template.BaseStats ?? [], StringComparer.Ordinal),
        EquippedSlot = slot,
        Value = template.BaseValue,
    };

    /// <summary>The equipped list, one short line per piece, for the report.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var item in Equipped.OrderBy(i => i.EquippedSlot))
        {
            yield return $"{item.EquippedSlot,-9} {item.TemplateKey}";
        }
    }

    /// <summary>The main-hand weapon's swing delay, or the unarmed default.</summary>
    public int MainHandDelayPulses(ContentSet content) => DelayOf(content, ItemSlot.MainHand);

    /// <summary>The off-hand weapon's swing delay, or the unarmed default.</summary>
    public int OffHandDelayPulses(ContentSet content) => DelayOf(content, ItemSlot.OffHand);

    /// <summary>Whether the off hand holds something that can actually swing.</summary>
    public bool HasOffHandWeapon(ContentSet content) =>
        Template(content, ItemSlot.OffHand)?.AttackDelayPulses is not null;

    private int DelayOf(ContentSet content, ItemSlot slot) =>
        AttackTiming.Clamp(Template(content, slot)?.AttackDelayPulses);

    private BundleItemTemplate? Template(ContentSet content, ItemSlot slot)
    {
        var equippedKey = Equipped.FirstOrDefault(i => i.EquippedSlot == slot)?.TemplateKey;

        return equippedKey is null
            ? null
            : content.Items.FirstOrDefault(i => string.Equals(i.Key, equippedKey, StringComparison.Ordinal));
    }
}

/// <summary>Which grade of equipment a fight is measured in.</summary>
public enum GearTier
{
    /// <summary>
    /// The realm's shop and drop gear — what a player clearing the realm is assumed to hold.
    /// Excludes the epic line entirely.
    /// </summary>
    Standard,

    /// <summary>The same, with the Path's epic reward in hand.</summary>
    Epic,
}
