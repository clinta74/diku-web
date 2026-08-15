using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// The ability set a <b>fresh database</b> is seeded with. Not the live one.
/// </summary>
/// <remarks>
/// <b>Read this before changing a number here.</b> The <c>abilities</c> table is the source of
/// truth; this list is what a database with no rows in it is born with, exactly as the twelve
/// Millbrook rooms in <c>StarterWorldSeeder</c> are. Its one reader in the whole of <c>src</c> is
/// that seeder, planting abilities a database does not already have. The Engine — the entire cast
/// path — never touches it.
///
/// <b>So editing a cooldown here does not retune the game.</b> It changes what a *new* install
/// starts with. Every database that already holds the row keeps its own value, because the
/// reconcile only inserts what is missing — which is deliberate, and is what stops a restart from
/// reverting a builder's work. A retune reaches an existing server as a migration or an imported
/// bundle, never by editing this file.
///
/// The list stays in code rather than becoming a data file because a fresh install has to get
/// abilities from somewhere before anything exists to import them from, and because it is the set
/// the tests hold the validator against.
///
/// Below: one list, rather than a seeder that writes ability rows and a progression table that names
/// them. Those were separate and had drifted in both directions: four abilities were unlocked at
/// level 6 with no row behind them (<c>warden.parry</c>, <c>adept.amplify</c>,
/// <c>shade.shadowstep</c>, <c>hallow.restore</c>) so reaching level 6 granted something that
/// could not be cast, while three that *were* seeded appeared in no progression at all
/// (<c>warden.battle-fury</c>, <c>adept.weaken</c>, <c>shade.fortify</c>) and so were unlearnable
/// - which is the whole of Phase 5.2a's buffs and debuffs, unreachable in play.
///
/// Deriving both from this list is what makes that class of mismatch impossible rather than
/// merely fixed.
///
/// Identity comes from cost, cadence, and scaling rather than from a private list of effects: a
/// Warden hits reliably and endures, a Shade pays little and strikes fast, an Adept pays a lot for
/// a big slow hit, a Hallow mends more than it harms.
///
/// <b>Three of the four reach a whole room, and the Shade is the one that does not.</b> That used
/// to be two — an area ability is the strongest thing the executors can express, and spreading it
/// everywhere would cost every Path its shape. What changed is that the Warden's job past level 20
/// is holding a room rather than a target, and an area *taunt* is not an area *attack*: it buys no
/// damage and no survival, only the attention of everything present, which is the one thing that
/// Path exists to take. The Shade keeps none, because killing one thing properly is its shape.
/// </remarks>
public static class AbilityCatalogue
{
    /// <summary>One ability, and what it takes to learn it.</summary>
    /// <param name="Path">The Path that learns it.</param>
    /// <param name="UnlockLevel">The level at which it is granted.</param>
    /// <param name="Maintainable">
    /// Whether this ability is <em>meant</em> to be held up permanently — its duration may exceed
    /// its cooldown.
    /// </param>
    /// <remarks>
    /// <b>The general rule is that nothing outlasts its own cooldown</b>, because buffs refresh
    /// rather than stack, so a longer duration makes the cooldown do nothing. Ten of the eleven
    /// timed effects were in that state once and the retune that fixed it is why the rule exists.
    ///
    /// <b>Hallow's group buffs are the deliberate exception, and it is what makes the Path a
    /// buffer without making buffing its combat rotation.</b> The point of a long duration and a
    /// short cooldown together is that the group is set up <em>before</em> the fight and the buffs
    /// are still standing at the end of it — so the Hallow spends the fight healing, which is the
    /// other half of the job, instead of re-casting protection it already gave.
    ///
    /// That sets the duration floor: a maintainable buff must comfortably outlast a whole fight,
    /// or it becomes exactly the in-combat chore it exists to avoid. The short cooldown is so that
    /// setting up a group is not itself a chore.
    ///
    /// A self-buff held up forever is still free power, so this is granted to group protection and
    /// withheld from anything that raises damage or personal survival. It is catalogue metadata
    /// rather than a column: the <c>abilities</c> table has no such field and the engine has no
    /// opinion — it exists so the design rule can be tested against the shipped set, exactly as
    /// the combat-beat rule on cooldowns is.
    /// </remarks>
    public sealed record Entry(
        CharacterPath Path,
        int UnlockLevel,
        string Key,
        string Name,
        string Description,
        CostType CostType,
        int CostValue,
        long CooldownPulses,
        long? CastTimePulses,
        TargetingType TargetingType,
        List<AbilityEffectSpec> Effects,
        bool Maintainable = false);

    /// <summary>One effect, which is what all thirty-seven starter abilities have.</summary>
    /// <summary>One effect, which is what most starter abilities have.</summary>
    private static List<AbilityEffectSpec> Effect(string key, Dictionary<string, string> parameters) =>
        [new(key, parameters)];

    /// <summary>
    /// Several effects, applied in order. All of them land - this is what the ability does, not a
    /// choice between them.
    /// </summary>
    /// <remarks>
    /// Named <c>Together</c> rather than <c>Effects</c> because that is the name of the namespace
    /// the executors live in, and the collision reads as a compiler error rather than as a choice.
    /// </remarks>
    private static List<AbilityEffectSpec> Together(params AbilityEffectSpec[] specs) => [.. specs];

    /// <summary>One entry in a multi-effect list.</summary>
    private static AbilityEffectSpec Part(string key, Dictionary<string, string> parameters) =>
        new(key, parameters);

    /// <summary>
    /// Raises the bearer's maximum health, and hands them that much health with it.
    /// </summary>
    /// <remarks>
    /// The grant happens once, on first application - <c>WorldState.ApplyEffect</c> enforces that,
    /// because only it can tell a fresh application from a refresh. Without the grant the buff
    /// would do nothing at the moment it is cast: 40/100 becoming 40/150 is further from safety.
    /// </remarks>
    private static Dictionary<string, string> MaxHealth(string amount, string duration, string name) =>
        new(StringComparer.Ordinal)
        {
            ["maxHealth"] = amount,
            ["durationPulses"] = duration,
            ["name"] = name,
        };

    /// <summary>
    /// Harder to hit, and blows that land cost less. Used by <c>debuff.expose</c> to take both away.
    /// </summary>
    /// <param name="mitigation">
    /// Whole percentage points of each landed blow, added to what the bearer's armour already
    /// absorbs and clamped with it at <see cref="Combat.ArmorCurve.Cap"/>. Percentage points rather
    /// than the flat amount this used to carry, so a guard is worth the same in Ossara as in the
    /// Unlit instead of being decisive at level 5 and unnoticeable at level 50.
    /// </param>
    private static Dictionary<string, string> Guard(
        string defenseRating,
        string mitigation,
        string duration,
        string name) =>
        new(StringComparer.Ordinal)
        {
            ["defenseRating"] = defenseRating,
            ["mitigation"] = mitigation,
            ["durationPulses"] = duration,
            ["name"] = name,
        };

    private static Dictionary<string, string> Damage(string scaling, string min) =>
        new(StringComparer.Ordinal) { ["scalingFactor"] = scaling, ["minDamage"] = min };

    private static Dictionary<string, string> Heal(string amount) =>
        new(StringComparer.Ordinal) { ["baseHeal"] = amount };

    /// <summary>
    /// A damage-up buff on the caster. The key is <c>outgoingMultiplier</c> because that is what
    /// <c>BuffEffect</c> reads - a parameter it does not recognise is skipped in silence, so a
    /// plausible-looking name like "magnitude" would produce a buff that did nothing.
    /// </summary>
    private static Dictionary<string, string> Buff(string outgoing, string duration, string name) =>
        new(StringComparer.Ordinal)
        {
            ["outgoingMultiplier"] = outgoing,
            ["durationPulses"] = duration,
            ["maxStacks"] = "1",
            ["stackingRule"] = "Refresh",
            ["name"] = name,
        };

    /// <summary>
    /// Takes the fight out of the target: it deals <paramref name="outgoing"/> of its usual
    /// damage. <b>Below 1.0</b> - 0.7 means it hits for 70% of what it would have.
    /// </summary>
    /// <remarks>
    /// The direction is the whole of the danger here. These multipliers are applied as-is, so a
    /// value on the wrong side of 1.0 silently helps whoever it was cast at - which is exactly
    /// what happened when these were first written against <c>incomingMultiplier</c>: every
    /// "weaken" in the game made its target take 25-45% *less* damage.
    /// </remarks>
    private static Dictionary<string, string> Weaken(string outgoing, string duration, string name) =>
        new(StringComparer.Ordinal)
        {
            ["outgoingMultiplier"] = outgoing,
            ["durationPulses"] = duration,
            ["maxStacks"] = "1",
            ["stackingRule"] = "Refresh",
            ["name"] = name,
        };

    /// <summary>
    /// Opens the target up: it takes <paramref name="incoming"/> of the damage it otherwise
    /// would. <b>Above 1.0</b> - 1.3 means everything lands for 30% more.
    /// </summary>
    private static Dictionary<string, string> Vulnerable(string incoming, string duration, string name) =>
        new(StringComparer.Ordinal)
        {
            ["incomingMultiplier"] = incoming,
            ["durationPulses"] = duration,
            ["maxStacks"] = "1",
            ["stackingRule"] = "Refresh",
            ["name"] = name,
        };

    /// <summary>
    /// A wound that keeps working: <paramref name="damage"/> every <paramref name="interval"/>
    /// pulses until it runs out.
    /// </summary>
    /// <remarks>
    /// Total damage is <c>damage × (duration / interval)</c>, and none of it lands on the cast
    /// itself - so a bleed is worth more the earlier it goes on, and worth nothing at all against
    /// something about to die. That is the point of it: it rewards a different decision than a
    /// bigger number would.
    ///
    /// It only ticks during a fight, because the ticker lives in the combat loop where the death,
    /// XP, and loot paths already are. Fleeing stops the bleeding.
    /// </remarks>
    private static Dictionary<string, string> OverTime(
        string damage,
        string interval,
        string duration,
        string name,
        string maxStacks = "1") =>
        new(StringComparer.Ordinal)
        {
            ["tickDamage"] = damage,
            ["tickIntervalPulses"] = interval,
            ["durationPulses"] = duration,
            ["maxStacks"] = maxStacks,
            ["stackingRule"] = maxStacks == "1" ? "Refresh" : "Stack",
            ["name"] = name,
        };

    /// <summary>
    /// Takes the target off its feet for <paramref name="duration"/> pulses: no swings, no casts,
    /// and anything it was casting breaks.
    /// </summary>
    /// <remarks>
    /// <c>StunEffect</c> clamps the duration to its own ceiling, so a typo that added a zero is a
    /// short stun rather than an opponent removed from the game.
    /// </remarks>
    private static Dictionary<string, string> Stun(string duration, string name) =>
        new(StringComparer.Ordinal)
        {
            ["durationPulses"] = duration,
            ["name"] = name,
        };

    /// <summary>
    /// Takes the target's attention off whoever currently has it and puts it on the caster, by
    /// <paramref name="lead"/> of the target's health bar's worth of threat.
    /// </summary>
    /// <remarks>
    /// A lead rather than a lock: the hate list is still cumulative damage afterwards, so whoever
    /// was displaced climbs back by out-damaging the taunter from here. Expressed as a fraction
    /// of the target's health because threat grows without bound over a fight - a flat number
    /// would be decisive in the first ten seconds and beneath notice five minutes in.
    /// </remarks>
    private static Dictionary<string, string> TauntLead(string lead) =>
        new(StringComparer.Ordinal) { ["leadFraction"] = lead };

    /// <summary>
    /// Holds the target where it stands for <paramref name="duration"/> pulses: it can still
    /// fight, but it cannot flee or walk away.
    /// </summary>
    /// <remarks>
    /// Clamped by <c>RootEffect</c> for the same reason as the stun. What it denies is <c>flee</c>
    /// - ordinary movement is already refused mid-fight, so blocking only that would do nothing
    /// in the situation a snare is cast in.
    /// </remarks>
    private static Dictionary<string, string> Root(string duration, string name) =>
        new(StringComparer.Ordinal)
        {
            ["durationPulses"] = duration,
            ["name"] = name,
        };

    /// <summary>
    /// The whole catalogue, ordered by Path then unlock level.
    /// </summary>
    /// <remarks>
    /// Unlocks land every two or three levels to level 20, so a level-up is usually worth
    /// something. Progression used to stop at level 6 for every Path, which is the reason
    /// levelling past it felt empty: there was nothing left to earn.
    /// </remarks>
    public static IReadOnlyList<Entry> All { get; } =
    [
        // -------------------------------------------------------------------
        // Warden - armored frontline. Stamina, short cooldowns, self-sustain.
        // -------------------------------------------------------------------
        // A kick rather than a slash: the opener must not assume a blade. A Warden with a mace,
        // a staff, or empty hands was still being told they slashed.
        new(CharacterPath.Warden, 1, "warden.kick", "Kick",
            "A boot to the knee. Nothing elegant, and it does not care what you are holding.",
            CostType.Stamina, 10, 24, null, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("1.1", "3")))),

        new(CharacterPath.Warden, 3, "warden.bash", "Bash",
            "Put your shoulder behind it. Slower, and it lands heavier.",
            CostType.Stamina, 15, 32, null, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("1.4", "5")))),

        new(CharacterPath.Warden, 5, "warden.battle-fury", "Battle Fury",
            "Anger sharpens the next stretch of a fight.",
            CostType.Stamina, 18, 160, null, TargetingType.Self,
            (Effect("buff.damage-up", Buff("1.25", "80", "battle fury")))),

        // Parry used to sit here as a castable self-heal. It is a passive now (PassiveKeys.Parry,
        // Warden 4 / Shade 8), because turning a blow aside is something a fighter does
        // continuously rather than something they stop to do - and as an ability it had to be
        // spent *before* the blow it was meant to stop.
        new(CharacterPath.Warden, 7, "warden.sunder", "Sunder",
            "Batter the guard apart. What comes next lands on what is left of it.",
            CostType.Stamina, 16, 160, null, TargetingType.SingleTarget,
            (Effect("debuff.weaken", Vulnerable("1.3", "80", "sundered")))),

        // The Warden's window-opener, and the one thing no amount of damage scaling expresses.
        // Kick stays instant damage at level 1 - this is the ability that had to be added rather
        // than the opener being repurposed, so a Warden keeps a cheap reliable hit *and* gains a
        // tempo tool.
        new(CharacterPath.Warden, 9, "warden.shield-bash", "Shield Bash",
            "The flat of the shield, hard, into whatever is nearest to a jaw.",
            CostType.Stamina, 20, 160, null, TargetingType.SingleTarget,
            (Effect("control.stun", Stun("16", "reeling")))),

        // The Warden's reason to exist in a group. Everything else on this Path survives damage;
        // this is the only thing that decides who *takes* it. Cheap and on a short cooldown,
        // because holding a mob is a job rather than a moment - and because the ability it is
        // answering, an Adept's biggest cast, comes back around too.
        new(CharacterPath.Warden, 8, "warden.taunt", "Taunt",
            "Say something unforgivable about its mother, at volume.",
            CostType.Stamina, 12, 32, null, TargetingType.SingleTarget,
            (Effect("control.taunt", TauntLead("0.30")))),

        new(CharacterPath.Warden, 10, "warden.rally", "Rally",
            "Find your feet again in the middle of it.",
            CostType.Stamina, 22, 96, 4, TargetingType.Self,
            (Effect("heal.restore", Heal("40")))),

        new(CharacterPath.Warden, 13, "warden.shield-wall", "Shield Wall",
            "Set yourself. Nothing moves you for a while.",
            CostType.Stamina, 25, 240, null, TargetingType.Self,
            (Effect("buff.damage-up", Buff("1.4", "100", "shield wall")))),

        new(CharacterPath.Warden, 16, "warden.crushing-blow", "Crushing Blow",
            "One heavy swing, wound up and committed to.",
            CostType.Stamina, 30, 48, 4, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("2.0", "12")))),

        // Two effects, which is the ability this could not previously be. It was authored as a
        // heal because one effect slot was all there was, and a heal is a different thing: it
        // undoes damage already taken rather than making the next stretch survivable. Now it does
        // what the name says - more health to lose, and harder to take it off you - and the
        // maximum comes back down when it ends, taking anything above the new ceiling with it.
        new(CharacterPath.Warden, 20, "warden.last-stand", "Last Stand",
            "Refuse to fall. The refusal is most of it.",
            CostType.Stamina, 30, 2400, null, TargetingType.Self,
            Together(
                Part("buff.max-health", MaxHealth("1000", "480", "standing ground")),
                Part("buff.defense", Guard("6", "6", "480", "standing ground")))),

        // -------------------------------------------------------------------
        // Warden, past 20 - holding a room rather than a target.
        //
        // Everything here is about taking hits *for other people*, which is a different job from
        // surviving them, and the one the list stopped short of: `taunt` is single-target and
        // arrives at level 8, and nothing after it helped hold more than one thing.
        // -------------------------------------------------------------------

        new(CharacterPath.Warden, 24, "warden.thunderclap", "Thunderclap",
            "Shield into the ground, once. Everything in the room decides you are the problem.",
            CostType.Stamina, 28, 96, null, TargetingType.Aoe,
            Effect("control.taunt", TauntLead("0.35"))),

        new(CharacterPath.Warden, 28, "warden.bulwark", "Bulwark",
            "Everything behind you is behind you.",
            CostType.Stamina, 32, 320, null, TargetingType.Self,
            Effect("buff.defense", Guard("8", "8", "240", "bulwark"))),

        new(CharacterPath.Warden, 32, "warden.ground-and-centre", "Ground and Centre",
            "More of you to get through, and less give in any of it.",
            CostType.Stamina, 36, 400, null, TargetingType.Self,
            Together(
                Part("buff.max-health", MaxHealth("120", "320", "grounded")),
                Part("buff.defense", Guard("6", "6", "320", "grounded")))),

        new(CharacterPath.Warden, 36, "warden.reprisal", "Reprisal",
            "Answer it, and make sure it noticed who did.",
            CostType.Stamina, 30, 64, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("1.6", "10")),
                Part("control.taunt", TauntLead("0.25")))),

        new(CharacterPath.Warden, 40, "warden.unbreakable", "Unbreakable",
            "Decide not to. The fight can disagree for a while.",
            CostType.Stamina, 45, 1200, null, TargetingType.Self,
            Together(
                Part("buff.max-health", MaxHealth("200", "480", "unbreakable")),
                Part("buff.defense", Guard("12", "12", "480", "unbreakable")))),

        new(CharacterPath.Warden, 43, "warden.sundering-blow", "Sundering Blow",
            "Take the guard apart so that everyone else's work lands on what is left.",
            CostType.Stamina, 34, 120, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("1.9", "12")),
                Part("debuff.expose", Guard("6", "5", "96", "sundered open")))),

        new(CharacterPath.Warden, 46, "warden.mass-provocation", "Mass Provocation",
            "Say the unforgivable thing to the whole room, and mean every word of it.",
            CostType.Stamina, 40, 320, null, TargetingType.Aoe,
            Together(
                Part("control.taunt", TauntLead("0.5")),
                Part("debuff.weaken", Weaken("0.8", "240", "cowed")))),

        new(CharacterPath.Warden, 50, "warden.the-last-wall", "The Last Wall",
            "There is a place past which things do not go. You are standing on it.",
            CostType.Stamina, 50, 2400, null, TargetingType.Self,
            Together(
                Part("buff.max-health", MaxHealth("400", "600", "the last wall")),
                Part("buff.defense", Guard("18", "18", "600", "the last wall")))),

        // -------------------------------------------------------------------
        // Adept - focus caster. Expensive, slow, and hits hardest at range.
        // -------------------------------------------------------------------
        new(CharacterPath.Adept, 1, "adept.bolt", "Bolt",
            "A thrown splinter of raw force.",
            CostType.Focus, 15, 24, 8, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("1.2", "4")))),

        new(CharacterPath.Adept, 3, "adept.shield", "Arcane Shield",
            "A shell of ordered air, briefly.",
            CostType.Focus, 12, 24, null, TargetingType.Self,
            (Effect("heal.restore", Heal("20")))),

        new(CharacterPath.Adept, 5, "adept.weaken", "Weaken",
            "Unpick the strength out of something.",
            CostType.Focus, 16, 160, 4, TargetingType.SingleTarget,
            (Effect("debuff.weaken", Weaken("0.75", "80", "weakened")))),

        new(CharacterPath.Adept, 7, "adept.amplify", "Amplify",
            "Wind the next few strikes tighter.",
            CostType.Focus, 20, 200, null, TargetingType.Self,
            (Effect("buff.damage-up", Buff("1.35", "80", "amplified")))),

        // The Adept's burn: slower and heavier per tick than the Shade's bleed, and it does not
        // stack - one big fire rather than several small cuts.
        new(CharacterPath.Adept, 10, "adept.scorch", "Scorch",
            "Heat with intent behind it, and nowhere for the heat to go.",
            CostType.Focus, 24, 72, 8, TargetingType.SingleTarget,
            (Effect("damage.overtime", OverTime("9", "12", "72", "burning")))),

        new(CharacterPath.Adept, 13, "adept.enfeeble", "Enfeeble",
            "Take the fight out of it at the root.",
            CostType.Focus, 26, 240, 4, TargetingType.SingleTarget,
            (Effect("debuff.weaken", Weaken("0.6", "100", "enfeebled")))),

        new(CharacterPath.Adept, 16, "adept.disjunction", "Disjunction",
            "Pull something apart along the seams it did not know it had.",
            CostType.Focus, 34, 56, 12, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("2.2", "14")))),

        // The first harmful area ability in the game, and the Adept's alone: a caster who can
        // answer a whole room is what the Path is for, and handing it to more than one would
        // flatten the distinction. It costs nearly double Disjunction and sits on a four-minute
        // cooldown, because an AoE pays once and lands many times. Its per-target scaling is
        // deliberately below the Path's level-1 Bolt - the value is in the count, not the number.
        new(CharacterPath.Adept, 18, "adept.firestorm", "Firestorm",
            "Fill the room with fire and let it decide what burns.",
            CostType.Focus, 60, 240, 20, TargetingType.Aoe,
            (Effect("damage.physical", Damage("1.3", "6")))),

        new(CharacterPath.Adept, 20, "adept.cataclysm", "Cataclysm",
            "The long words. Slow to say, and worth saying.",
            CostType.Focus, 45, 192, 16, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("3.0", "25")))),

        // -------------------------------------------------------------------
        // Adept, past 20 - from one target to the room.
        //
        // Firestorm and Cataclysm already established the direction at 18 and 20; what was missing
        // was everything after them. Nothing here is subtle: the Path pays a lot and hits hard.
        // -------------------------------------------------------------------

        new(CharacterPath.Adept, 24, "adept.conflagration", "Conflagration",
            "Set the air going and let it keep going.",
            CostType.Focus, 50, 200, 8, TargetingType.Aoe,
            Effect("damage.overtime", OverTime("14", "12", "96", "conflagration"))),

        new(CharacterPath.Adept, 28, "adept.shatter", "Shatter",
            "Find the fault, and put everything into it.",
            CostType.Focus, 34, 120, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("2.1", "14")),
                Part("debuff.expose", Guard("5", "5", "96", "shattered")))),

        new(CharacterPath.Adept, 32, "adept.chain-lightning", "Chain Lightning",
            "It picks its own way across the room and is not slow about it.",
            CostType.Focus, 48, 160, 8, TargetingType.Aoe,
            Effect("damage.physical", Damage("1.8", "12"))),

        new(CharacterPath.Adept, 36, "adept.unmaking", "Unmaking",
            "Take away some of what it is, and it hits like something less.",
            CostType.Focus, 40, 160, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("2.2", "15")),
                Part("debuff.weaken", Weaken("0.7", "120", "unmade")))),

        new(CharacterPath.Adept, 40, "adept.pyre", "Pyre",
            "Everything in the room, twice - once now and once for a while afterwards.",
            CostType.Focus, 60, 240, 12, TargetingType.Aoe,
            Together(
                Part("damage.physical", Damage("2.0", "14")),
                Part("damage.overtime", OverTime("18", "12", "120", "pyre")))),

        new(CharacterPath.Adept, 43, "adept.arcane-surge", "Arcane Surge",
            "The window you fire the long words through.",
            CostType.Focus, 38, 240, null, TargetingType.Self,
            Effect("buff.damage-up", Buff("1.6", "160", "surging"))),

        new(CharacterPath.Adept, 46, "adept.gravity-well", "Gravity Well",
            "Nothing in the room is going anywhere, and standing there is costing it.",
            CostType.Focus, 55, 320, 12, TargetingType.Aoe,
            Together(
                Part("control.root", Root("32", "held under")),
                Part("damage.overtime", OverTime("20", "16", "64", "crushed")))),

        new(CharacterPath.Adept, 50, "adept.the-unwriting", "The Unwriting",
            "Say what a thing is not, at sufficient volume.",
            CostType.Focus, 70, 600, 16, TargetingType.Aoe,
            Together(
                Part("damage.physical", Damage("3.0", "20")),
                Part("debuff.expose", Guard("8", "8", "96", "unwritten")))),

        // -------------------------------------------------------------------
        // Shade - stealth and burst. Cheap, fast, and fragile.
        // -------------------------------------------------------------------
        new(CharacterPath.Shade, 1, "shade.strike", "Quick Strike",
            "In and out before it turns.",
            CostType.Stamina, 12, 24, null, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("1.25", "4")))),

        new(CharacterPath.Shade, 3, "shade.evasion", "Evasion",
            "Not being where the blow lands.",
            CostType.Stamina, 10, 16, null, TargetingType.Self,
            (Effect("heal.restore", Heal("15")))),

        new(CharacterPath.Shade, 5, "shade.fortify", "Fortify",
            "Settle your grip and pick the angle.",
            CostType.Stamina, 14, 176, null, TargetingType.Self,
            (Effect("buff.damage-up", Buff("1.3", "72", "fortified")))),

        // Shadowstep was a third flat damage number on a Path that already had several. A
        // hamstring is the thing an assassin actually wants and nothing else in the game does:
        // it decides whether the fight ends, rather than how fast.
        new(CharacterPath.Shade, 7, "shade.hamstring", "Hamstring",
            "Cut low. Whatever it was going to do next, it is not going anywhere.",
            CostType.Stamina, 16, 128, null, TargetingType.SingleTarget,
            (Effect("control.root", Root("32", "hamstrung")))),

        // Ambush is the Shade's bleed rather than another number: applied early it out-damages
        // the burst it replaced, and applied late it does almost nothing. Stacks to three, so
        // the fast, cheap Path has something to spend its speed on.
        new(CharacterPath.Shade, 10, "shade.ambush", "Ambush",
            "Open something that will not close on its own.",
            CostType.Stamina, 20, 16, null, TargetingType.SingleTarget,
            (Effect("damage.overtime", OverTime("5", "8", "48", "bleeding", maxStacks: "3")))),

        // A Shade's version: later, dearer, and a smaller lead than the Warden's. It is the
        // off-tank's tool rather than the tank's - enough to take a mob for a while, not enough
        // to make the Path a substitute for one that can survive holding it.
        new(CharacterPath.Shade, 12, "shade.provoke", "Provoke",
            "A cut where it will be noticed, and a look daring it to do something about it.",
            CostType.Stamina, 18, 48, null, TargetingType.SingleTarget,
            (Effect("control.taunt", TauntLead("0.18")))),

        new(CharacterPath.Shade, 13, "shade.vanish", "Vanish",
            "Break away and let them lose you.",
            CostType.Stamina, 18, 72, null, TargetingType.Self,
            (Effect("heal.restore", Heal("45")))),

        new(CharacterPath.Shade, 16, "shade.assassinate", "Assassinate",
            "One place, once, properly.",
            CostType.Stamina, 28, 64, 4, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("2.4", "16")))),

        new(CharacterPath.Shade, 20, "shade.death-mark", "Death Mark",
            "Decide how this ends, then make it true.",
            CostType.Stamina, 35, 192, null, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("2.8", "22")))),

        // -------------------------------------------------------------------
        // Shade, past 20 - burst now, bleeding after.
        //
        // The two halves are the whole design: put the wound on, then spend the burst while it
        // works. Deliberately no area ability - a Shade kills one thing properly, and spreading
        // the room-wide effects across every Path would cost each of them its shape.
        // -------------------------------------------------------------------

        new(CharacterPath.Shade, 24, "shade.rupture", "Rupture",
            "Something inside it is now outside the arrangement.",
            CostType.Stamina, 26, 64, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("1.5", "8")),
                Part("damage.overtime", OverTime("10", "8", "64", "ruptured")))),

        new(CharacterPath.Shade, 28, "shade.exploit", "Exploit",
            "Cheap, fast, and it makes the next one worse for them.",
            CostType.Stamina, 22, 72, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("1.4", "8")),
                Part("debuff.expose", Guard("4", "4", "64", "exploited")))),

        new(CharacterPath.Shade, 32, "shade.flurry", "Flurry",
            "More than it can count, in less time than it has.",
            CostType.Stamina, 24, 48, null, TargetingType.SingleTarget,
            Effect("damage.physical", Damage("1.7", "10"))),

        new(CharacterPath.Shade, 36, "shade.hemorrhage", "Hemorrhage",
            "Cheap to start and expensive to be on the wrong end of.",
            CostType.Stamina, 28, 96, null, TargetingType.SingleTarget,
            Effect("damage.overtime", OverTime("16", "8", "96", "hemorrhaging"))),

        new(CharacterPath.Shade, 40, "shade.execution", "Execution",
            "The whole fight, arriving at once and slightly early.",
            CostType.Stamina, 45, 240, null, TargetingType.SingleTarget,
            Effect("damage.physical", Damage("3.2", "20"))),

        new(CharacterPath.Shade, 43, "shade.shadowstep", "Shadowstep",
            "Be somewhere else, then be behind it.",
            CostType.Stamina, 32, 160, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("1.8", "10")),
                Part("control.stun", Stun("16", "reeling")))),

        // Stacks to five, which is what makes it a rotation rather than a refresh: the cooldown is
        // deliberately shorter than the duration so the cuts pile up on something that lives long
        // enough to regret it. The permanence rule skips multi-stack effects for exactly this.
        new(CharacterPath.Shade, 46, "shade.thousand-cuts", "A Thousand Cuts",
            "None of them would have done it. All of them will.",
            CostType.Stamina, 20, 32, null, TargetingType.SingleTarget,
            Effect("damage.overtime", OverTime("12", "8", "72", "cut to pieces", maxStacks: "5"))),

        new(CharacterPath.Shade, 50, "shade.severance", "Severance",
            "Take the fight out of it and the rest follows on its own.",
            CostType.Stamina, 55, 480, null, TargetingType.SingleTarget,
            Together(
                Part("damage.physical", Damage("3.5", "22")),
                Part("damage.overtime", OverTime("22", "8", "120", "severed")),
                Part("debuff.weaken", Weaken("0.75", "120", "severed")))),

        // -------------------------------------------------------------------
        // Hallow - support and control. Mends more than it harms.
        //
        // Every supportive ability here is SingleTarget, not Self. A support Path whose heals
        // only reach itself is not a support Path - and casting one with no target named still
        // lands on the caster, because a helpful ability falls back to "me" rather than to
        // whatever is currently being fought.
        // -------------------------------------------------------------------
        new(CharacterPath.Hallow, 1, "hallow.mend", "Mend",
            "Close what is open, on yourself or on someone beside you.",
            CostType.Focus, 20, 24, 4, TargetingType.SingleTarget,
            (Effect("heal.restore", Heal("25")))),

        new(CharacterPath.Hallow, 3, "hallow.guidance", "Guidance",
            "Steady a hand that is about to need steadying.",
            CostType.Focus, 15, 24, null, TargetingType.SingleTarget,
            (Effect("heal.restore", Heal("18")))),

        // The Hallow's wither: the longest of the three and the slowest to pay out, which
        // suits a Path that wins by outlasting rather than by out-hitting. Sap at 16 keeps the
        // Path's weaken, so this does not cost it its control identity.
        new(CharacterPath.Hallow, 5, "hallow.wither", "Wither",
            "Set something going that will not stop on its own.",
            CostType.Focus, 18, 96, 4, TargetingType.SingleTarget,
            (Effect("damage.overtime", OverTime("6", "16", "96", "withering")))),

        new(CharacterPath.Hallow, 7, "hallow.restore", "Restore",
            "Put back what the fight has taken so far.",
            CostType.Focus, 28, 40, 8, TargetingType.SingleTarget,
            (Effect("heal.restore", Heal("50")))),

        new(CharacterPath.Hallow, 10, "hallow.blessing", "Blessing",
            "Lend the next while a better edge than it earned.",
            CostType.Focus, 24, 240, null, TargetingType.SingleTarget,
            (Effect("buff.damage-up", Buff("1.3", "96", "blessed")))),

        new(CharacterPath.Hallow, 13, "hallow.renewal", "Renewal",
            "Begin again, without stopping.",
            CostType.Focus, 34, 64, 8, TargetingType.SingleTarget,
            (Effect("heal.restore", Heal("70")))),

        new(CharacterPath.Hallow, 16, "hallow.sap", "Sap",
            "Take the strength and do not give it back.",
            CostType.Focus, 30, 240, 4, TargetingType.SingleTarget,
            (Effect("debuff.weaken", Weaken("0.55", "100", "sapped")))),

        // The other half of area targeting, and the reason the filter has two directions: a
        // helpful AoE gathers the caster and everyone standing with them rather than the things
        // they are fighting. Until parties exist (5.3) "the room" is the closest honest reading of
        // "your side", which is generous - and generous is the safe direction for a heal.
        new(CharacterPath.Hallow, 18, "hallow.benediction", "Benediction",
            "Say it over everyone at once, and mean it.",
            CostType.Focus, 55, 200, 16, TargetingType.Aoe,
            (Effect("heal.restore", Heal("55")))),

        new(CharacterPath.Hallow, 20, "hallow.intercession", "Intercession",
            "Stand between someone and what was coming for them.",
            CostType.Focus, 50, 176, 12, TargetingType.SingleTarget,
            (Effect("heal.restore", Heal("120")))),

        // -------------------------------------------------------------------
        // Hallow, past 20 - keeping people alive, and keeping them standing.
        //
        // Healing undoes damage; health and protection change how much damage there is to undo.
        // The back half is built so the Path does both, and so the *buffing* happens before the
        // fight rather than during it: the protections are marked Maintainable and last long
        // enough to still be standing at the end, which is what leaves the Hallow free to spend
        // the fight healing.
        //
        // Deliberately no new damage. Wither and Sap already make the Path playable alone, and
        // adding more would make it a worse Adept in the one place it should be irreplaceable.
        // -------------------------------------------------------------------

        new(CharacterPath.Hallow, 24, "hallow.communion", "Communion",
            "Say it once, for everyone standing with you.",
            CostType.Focus, 55, 64, 8, TargetingType.Aoe,
            Effect("heal.restore", Heal("70"))),

        new(CharacterPath.Hallow, 28, "hallow.fortitude", "Fortitude",
            "More of everyone to get through. Set it before you go in.",
            CostType.Focus, 55, 96, 8, TargetingType.Aoe,
            Effect("buff.max-health", MaxHealth("150", "1200", "fortified")),
            Maintainable: true),

        new(CharacterPath.Hallow, 32, "hallow.aegis", "Aegis",
            "Something between the group and the weather.",
            CostType.Focus, 55, 96, 8, TargetingType.Aoe,
            Effect("buff.defense", Guard("7", "6", "1200", "warded")),
            Maintainable: true),

        new(CharacterPath.Hallow, 36, "hallow.mending-tide", "Mending Tide",
            "The one that comes in over everyone at once, when Communion was not enough.",
            CostType.Focus, 60, 160, 12, TargetingType.Aoe,
            Effect("heal.restore", Heal("140"))),

        new(CharacterPath.Hallow, 40, "hallow.sanctuary", "Sanctuary",
            "Draw the line around all of them at once, and make it hold.",
            CostType.Focus, 70, 240, 12, TargetingType.Aoe,
            Together(
                Part("buff.max-health", MaxHealth("220", "1200", "sanctified")),
                Part("buff.defense", Guard("10", "9", "1200", "sanctified"))),
            Maintainable: true),

        new(CharacterPath.Hallow, 43, "hallow.absolution", "Absolution",
            "Everything, off one person, now.",
            CostType.Focus, 55, 96, null, TargetingType.SingleTarget,
            Effect("heal.restore", Heal("260"))),

        new(CharacterPath.Hallow, 46, "hallow.consecration", "Consecration",
            "Support that is not mending: the whole group hits like it means it.",
            CostType.Focus, 60, 160, 8, TargetingType.Aoe,
            Effect("buff.damage-up", Buff("1.35", "960", "consecrated")),
            Maintainable: true),

        new(CharacterPath.Hallow, 50, "hallow.the-long-vigil", "The Long Vigil",
            "Close what is open, raise what is left, and stand over all of it.",
            CostType.Focus, 80, 600, 16, TargetingType.Aoe,
            Together(
                Part("heal.restore", Heal("200")),
                Part("buff.max-health", MaxHealth("300", "1200", "the long vigil")),
                Part("buff.defense", Guard("12", "12", "1200", "the long vigil"))),
            Maintainable: true),
    ];

    /// <summary>Every ability this Path learns, in unlock order.</summary>
    public static IReadOnlyList<Entry> For(CharacterPath path) =>
        [.. All.Where(e => e.Path == path).OrderBy(e => e.UnlockLevel)];

    /// <summary>Builds the <see cref="Ability"/> row for an entry.</summary>
    /// <summary>
    /// Every (ability, effect) pair in the starter set.
    /// </summary>
    /// <remarks>
    /// An ability carries a list now, so a question about effects - "does every stun sit under its
    /// clamp" - is a question about this rather than about <see cref="All"/>. Iterating abilities
    /// and reaching for a first effect would answer it only for as long as nothing has two.
    /// </remarks>
    public static IEnumerable<(Entry Entry, AbilityEffectSpec Effect)> AllEffects =>
        All.SelectMany(entry => entry.Effects.Select(effect => (entry, effect)));

    /// <summary>
    /// The whole starter set as <see cref="Ability"/> rows — what a fresh database is seeded with.
    /// </summary>
    /// <remarks>
    /// <b>This is the starter set, not the live one.</b> Anything asking what abilities exist
    /// *now* must read the <c>abilities</c> table (through <c>AbilityCache</c> at runtime), because
    /// a builder can add, retune, and remove rows and none of that reaches this list. The
    /// legitimate callers are the seeder, which plants these on first boot, and tests that assert
    /// about the shipped set specifically.
    /// </remarks>
    public static IReadOnlyList<Ability> AsAbilities { get; } = [.. All.Select(ToAbility)];

    public static Ability ToAbility(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new Ability
        {
            Key = entry.Key,
            Path = entry.Path,
            UnlockLevel = entry.UnlockLevel,
            Name = entry.Name,
            Description = entry.Description,
            CostType = entry.CostType,
            CostValue = entry.CostValue,
            CooldownPulses = entry.CooldownPulses,
            CastTimePulses = entry.CastTimePulses,
            TargetingType = entry.TargetingType,
            Effects = [.. entry.Effects.Select(e =>
                new AbilityEffectSpec(e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))],
        };
    }
}
