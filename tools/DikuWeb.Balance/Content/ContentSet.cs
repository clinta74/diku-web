using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Server.Building;

namespace DikuWeb.Balance.Content;

/// <summary>
/// The authored world, loaded once and indexed for the simulator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read through <see cref="BundleFormat"/>, never with a reader of its own.</b> A second parser
/// is a second opinion about what an authored file means, and the one that is wrong is always the
/// one nobody runs. This is the same door <c>check-bundle</c> and the import endpoint go through.
/// </para>
/// <para>
/// <b>Every source is recorded, with its stamp.</b> Content is authored in the database and
/// <em>exported</em> to <c>content/</c>, so the files on disk are a snapshot that can be older than
/// the world being asked about — a rebalance applied to the database reaches this harness only when
/// somebody exports it. A balance report run against stale numbers is worse than no report, because
/// it looks exactly like a fresh one. <see cref="Describe"/> is printed at the top of every run so
/// the reader can see what was measured.
/// </para>
/// </remarks>
public sealed class ContentSet
{
    private ContentSet(
        IReadOnlyList<SourceStamp> sources,
        WorldBundle bundle,
        IReadOnlyDictionary<string, Ability> abilities,
        IReadOnlyList<Encounter> encounters,
        IReadOnlyList<BundleItemTemplate> items,
        IReadOnlyDictionary<string, string> itemRealms)
    {
        Sources = sources;
        Bundle = bundle;
        Abilities = abilities;
        Encounters = encounters;
        Items = items;
        ItemRealms = itemRealms;
    }

    /// <summary>
    /// Which realm each item template was authored in, by item key.
    /// </summary>
    /// <remarks>
    /// <b>Taken from the file it arrived in, because nothing on the item says it.</b> A template
    /// carries no level and no tier — the only thing that places a sword in the progression is
    /// which realm's file it sits in, and the merge that follows throws that away. Captured before
    /// the merge for exactly that reason.
    ///
    /// This is the one piece of the loadout model that is inferred rather than authored, so it is
    /// named here and printed in the report rather than buried in a picker.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ItemRealms { get; }

    /// <summary>What was loaded, and when each file claims to have been exported.</summary>
    public IReadOnlyList<SourceStamp> Sources { get; }

    public WorldBundle Bundle { get; }

    /// <summary>Every ability, by key, converted to the Domain shape the executors expect.</summary>
    public IReadOnlyDictionary<string, Ability> Abilities { get; }

    /// <summary>Every mob as it actually spawns somewhere — scaled, levelled, placed.</summary>
    public IReadOnlyList<Encounter> Encounters { get; }

    public IReadOnlyList<BundleItemTemplate> Items { get; }

    /// <summary>
    /// Loads and merges every bundle under the given paths. A path may be a file or a directory;
    /// a directory is searched recursively for <c>*.json</c>.
    /// </summary>
    public static ContentSet Load(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var files = new List<string>();

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories));
            }
            else
            {
                throw new FileNotFoundException($"No such file or directory: {path}");
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("No bundle files found.");
        }

        var sources = new List<BundleSource>();
        var stamps = new List<SourceStamp>();
        var realms = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files.OrderBy(p => p, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);

            if (!BundleFormat.TryRead(text, out var bundle, out var error))
            {
                throw new InvalidOperationException($"{file}: {error}");
            }

            sources.Add(new BundleSource(file, bundle!));
            stamps.Add(new SourceStamp(file, bundle!.FormatVersion, bundle.ExportedAt));

            // The directory a file sits in, captured before the merge loses it. Only a hint:
            // RealmIndex prefers what an item's own key says, because a whole-world export is one
            // file in no realm's directory at all.
            var directory = Path.GetFileName(Path.GetDirectoryName(file));

            if (!string.IsNullOrEmpty(directory) &&
                !string.Equals(directory, "content", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in bundle.ItemTemplates)
                {
                    realms[item.Key] = directory;
                }
            }
        }

        var merged = BundleMerge.Merge(sources);

        if (!merged.Ok)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, merged.Errors));
        }

        var whole = merged.Bundle!;

        return new ContentSet(
            stamps,
            whole,
            IndexAbilities(whole),
            BuildEncounters(whole),
            [.. whole.ItemTemplates],
            RealmIndex.Build(whole, realms));
    }

    /// <summary>One line per file, for the head of a report.</summary>
    public IEnumerable<string> Describe()
    {
        var newest = Sources.Max(s => s.ExportedAt);
        var age = DateTimeOffset.UtcNow - newest;

        yield return $"{Sources.Count} bundle(s), newest exported {newest:yyyy-MM-dd HH:mm} UTC " +
                     $"({(int)age.TotalDays}d ago)";

        if (age.TotalDays >= 1)
        {
            yield return "  NOTE: content/ is an export of the database, not the database. Anything";
            yield return "  edited in the builder since that stamp is NOT in this run. Re-export with";
            yield return "  `GET /api/builder/export` and pass --content <file> to measure it.";
        }
    }

    /// <summary>
    /// Bundle abilities as Domain abilities.
    /// </summary>
    /// <remarks>
    /// A bundle's nullable enums are "not stated", which for an ability is not a legal shape — the
    /// validator refuses one on the way in. Anything still missing here came from a file that never
    /// passed through it, so it is dropped rather than defaulted: a Path invented for an ability
    /// would put it in a kit its author never chose.
    /// </remarks>
    private static Dictionary<string, Ability> IndexAbilities(WorldBundle bundle)
    {
        var result = new Dictionary<string, Ability>(StringComparer.Ordinal);

        foreach (var a in bundle.Abilities)
        {
            if (a.Path is not { } path || a.CostType is not { } cost ||
                a.TargetingType is not { } targeting || a.Effects is null)
            {
                continue;
            }

            result[a.Key] = new Ability
            {
                Key = a.Key,
                Path = path,
                UnlockLevel = a.UnlockLevel,
                Name = a.Name,
                Description = a.Description,
                CostType = cost,
                CostValue = a.CostValue,
                CooldownPulses = a.CooldownPulses,
                CooldownGroup = a.CooldownGroup,
                CastTimePulses = a.CastTimePulses,
                TargetingType = targeting,
                Effects = a.Effects,
            };
        }

        return result;
    }

    /// <summary>
    /// Every (template, zone) pair a spawner actually creates, scaled the way
    /// <c>MobSpawner</c> scales it.
    /// </summary>
    /// <remarks>
    /// <b>Driven by spawners rather than by templates.</b> A template on its own has no difficulty:
    /// its level and health are a baseline that the zone it is placed in multiplies (PLAN.md §4.4),
    /// and the same template placed in two zones is two different fights. Reporting per template
    /// would have averaged those together and named the result after neither.
    ///
    /// Distinct on (template, zone) rather than per spawner, because a template placed by four
    /// spawners in one zone is one encounter listed four times.
    /// </remarks>
    private static List<Encounter> BuildEncounters(WorldBundle bundle)
    {
        var zones = bundle.Zones.ToDictionary(z => z.Key, StringComparer.Ordinal);
        var worlds = bundle.Worlds.ToDictionary(w => w.Key, StringComparer.Ordinal);
        var mobs = bundle.MobTemplates.ToDictionary(m => m.Key, StringComparer.Ordinal);

        var seen = new HashSet<(string, string, int?)>();
        var result = new List<Encounter>();

        foreach (var spawner in bundle.Spawners)
        {
            if (spawner.TemplateKind != TemplateKind.Mob ||
                !mobs.TryGetValue(spawner.TemplateKey, out var template) ||
                !zones.TryGetValue(spawner.ZoneKey, out var zone) ||
                !worlds.TryGetValue(zone.WorldKey, out var world) ||
                !seen.Add((spawner.TemplateKey, spawner.ZoneKey, spawner.FightsAtLevel)))
            {
                continue;
            }

            // A non-combatant is not an encounter. Shopkeepers, quest givers and turn-ins
            // "may neither attack nor be attacked" (MobBehavior.IsNonCombatant), so a harness
            // that fought one would be measuring a fight the game refuses to start - and they
            // are authored with real health and real levels, so nothing else here excludes them.
            //
            // Asked of MobBehavior rather than by reading the bag, because the default matters:
            // an unrecognised word reads as Passive, which is attackable, and a check written
            // here would have to get that right independently.
            if (MobBehavior.IsNonCombatant(template.Behavior))
            {
                continue;
            }

            var worldMults = ReadMultipliers(world.Multipliers);
            var zoneMults = ReadMultipliers(zone.Multipliers);

            // The pin replaces the zone's dials rather than composing with them (PLAN.md §4.7).
            // Mirrors MobSpawner exactly; a harness that composed them would report a doubled zone
            // as twice the difficulty the builder typed.
            var scaling = spawner.FightsAtLevel is { } pinned
                ? MobScaling.FromTarget(template.Level, pinned)
                : MobScaling.FromZone(template.Level, worldMults, zoneMults, zone.MinLevel);

            var baseHealth = ReadInt(template.BaseStats, "health", 40);
            var health = Math.Max(
                1, (int)Math.Round(baseHealth * scaling.Health, MidpointRounding.AwayFromZero));

            result.Add(new Encounter(
                TemplateKey: template.Key,
                Name: template.Name,
                WorldKey: zone.WorldKey,
                ZoneKey: zone.Key,
                ZoneMinLevel: zone.MinLevel,
                ZoneMaxLevel: zone.MaxLevel,
                AuthoredLevel: template.Level,
                Level: scaling.Level,
                Health: health,
                ResolvedStats: scaling.ResolveStats(template.BaseStats),
                Attacks: template.Attacks ?? []));
        }

        return [.. result.OrderBy(e => e.Level).ThenBy(e => e.Health)];
    }

    /// <summary>
    /// Mirrors <c>WorldImporter.Multipliers</c>: each factor read by name, an absent key meaning
    /// the identity. Duplicated because that one is private; it is six lines and a drift here would
    /// show up immediately as a mob at the wrong level.
    /// </summary>
    private static Multipliers ReadMultipliers(IReadOnlyDictionary<string, decimal>? values)
    {
        var m = new Multipliers();

        if (values is null)
        {
            return m;
        }

        var byName = new Dictionary<string, decimal>(values, StringComparer.OrdinalIgnoreCase);

        decimal Read(string key, decimal fallback) =>
            byName.TryGetValue(key, out var v) ? v : fallback;

        m.Strength = Read("strength", m.Strength);
        m.Health = Read("health", m.Health);
        m.Damage = Read("damage", m.Damage);
        m.Xp = Read("xp", m.Xp);
        m.Gold = Read("gold", m.Gold);
        m.ItemValue = Read("itemValue", m.ItemValue);

        return m;
    }

    private static int ReadInt(IReadOnlyDictionary<string, object>? stats, string key, int fallback) =>
        stats is not null && DikuWeb.Domain.Combat.StatReader.TryReadInt(stats, key, out var value)
            ? value
            : fallback;
}

/// <summary>Where a bundle came from and what it claims about its own freshness.</summary>
public sealed record SourceStamp(string Path, int FormatVersion, DateTimeOffset ExportedAt);

/// <summary>
/// One mob, as it actually stands in a room: scaled by its zone, at the level it fights at.
/// </summary>
public sealed record Encounter(
    string TemplateKey,
    string Name,
    string WorldKey,
    string ZoneKey,
    int ZoneMinLevel,
    int ZoneMaxLevel,
    int AuthoredLevel,
    int Level,
    int Health,
    Dictionary<string, object> ResolvedStats,
    IReadOnlyList<MobAttack> Attacks)
{
    /// <summary>
    /// A <see cref="Mob"/> carrying these numbers, so <c>DamageCalculator</c> can read it the way
    /// it reads a real one. Building the stats by hand here would be a second copy of
    /// <c>StatsFrom</c>'s fallback rules, which are most of what decides a silent template's punch.
    /// </summary>
    public Mob ToMob() => new()
    {
        Id = Guid.NewGuid(),
        TemplateKey = TemplateKey,
        TemplateName = Name,
        Level = AuthoredLevel,
        EffectiveLevel = Level,
        RoomKey = "balance:harness",
        ResolvedStats = ResolvedStats,
        Vitals = new Vitals
        {
            Health = Health,
            HealthMax = Health,
            Focus = 0,
            FocusMax = 0,
            Stamina = 0,
            StaminaMax = 0,
        },
    };
}
