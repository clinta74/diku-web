# Build history — Phases 0 through 5

The record of work that is finished. Moved out of [PLAN.md](PLAN.md) §8 on 2026-08-13, verbatim,
when that document had grown to 2,748 lines and most of the bulk was an account of bugs already
fixed. Nothing here is a plan; PLAN.md keeps the design and the open phases.

**Why it is kept at all.** Roughly half of these entries are postmortems, and the pattern in them
is the reason: a feature written, checked off, and wired to nothing; a jsonb bag read with a C#
type test that is false for every value storage returns; a verb shadowed by an earlier verb's
prefix. Each of those has happened more than once here. The two rules they produced live in
PLAN.md §12 — this file is the evidence behind them.

Section numbering follows PLAN.md §8, so a phase cited as *Phase 5.2d* is the heading of the same
name below.

---

## Phase 0 — Foundation ✅ **complete**
*Done when: `docker compose up`, `dotnet run`, and `npm run dev` all work, and `/health` is 200.*

- [x] Solution + all projects, `Directory.Build.props` (net10.0, nullable, warnings as errors)
- [x] Central package management in `Directory.Packages.props`
- [x] `docker-compose.yml` with postgres:18 + adminer, credentials in `.env`
- [x] EF Core + Npgsql wired, `accounts` + `characters`, initial migration applied
- [x] Logging per §2.4; config via `appsettings.*.json` + user-secrets
- [x] React + Vite client scaffold, dev proxy to the API
- [x] `IGameClock` + `IRandomSource` from the first commit (§9)
- [x] Test projects: 33 .NET tests + 3 client tests; Testcontainers on postgres:18
- [x] `.editorconfig`, `.gitignore`, README with local setup steps
- [x] Verified end to end: browser → Vite proxy → Kestrel → Postgres

Notes from the build:

- **PG 18 changed its Docker volume path.** The data directory moved to a major-version
  subdirectory, so the volume must mount at `/var/lib/postgresql`, not
  `/var/lib/postgresql/data` as it was through PG 17. Mounting the old path makes the
  container refuse to start outright.
- `Roll(...)` lives in `RandomSourceExtensions` rather than as a default interface member,
  because default members are only callable through the interface — implementations would not
  expose them, making every call site and test awkward.

## Phase 1 — Vertical slice ✅ **complete**
*Done when: two browsers log in, walk a code-seeded zone, see each other on the map, and talk.*

- [x] Register / login / logout, HttpOnly session cookie
- [x] Character creation (name, Path) and character list
- [x] Session registry: session ↔ character ↔ outbound channel
- [x] Game loop hosted service: 250 ms pulse, command drain, slow-pulse watchdog
- [x] SSE endpoint with heartbeat, `id:`, ring buffer, `Last-Event-ID` replay
- [x] Command parser: verb + args, prefix abbreviation (`n`, `lo`, `sa`), unknown-verb help
- [x] Commands: `look`, `north/south/east/west/up/down`, `say`, `who`, `quit`, `help`
- [x] World → Zone → Room model + `world.zone.room` key resolution
- [x] `RoomLayoutService`: deterministic cell assignment, `map` events
- [x] Architecture test: no coordinates in Domain, no layout access from command handlers
- [x] **Code seeder**: 12-room starter zone (Aldenmoor / Millbrook)
- [x] Client: all five game panels (§5), input history, icon legend
- [x] Save on quit and autosave; restore room on re-entry
- [x] Link-dead handling with 90 s grace window
- [x] 139 .NET tests + 8 client tests; two-player flow verified end to end

Notes from the build:

- **`mapdelta` was not needed yet.** With only players in the world, a full `map` on change is
  a few hundred bytes and simpler than diffing. Revisit in Phase 3 when mobs and ground items
  make rooms busy enough for the delta to earn its keep.
- **Zone multipliers (§4.4) are deliberately absent from the schema.** They are Phase 3 work and
  nothing in Phase 1 reads them; adding the column now would be speculative. It arrives as a
  migration alongside the code that uses it.
- **Saves hand over an immutable `CharacterSnapshot`, not the live entity.** The loop keeps
  mutating a `Character` while the save worker runs on another thread, so passing the object
  itself would be a data race — and the symptom would be a character saved with the room from
  one moment and the vitals from another.
- **`RoomKey` is a validated value type**, not a bare string. Two-segment keys and stray
  uppercase now fail at the boundary rather than becoming a room that silently never resolves.
- **Overflow placement stacks rather than drops.** A room with fewer walkable cells than
  occupants used to omit the extras from the map entirely — present in the room and the contents
  list, invisible on the grid. Overlapping icons are the better failure.

## Phase 2 — World builder: geography ✅ **complete**
*Done when: a new zone can be built end to end in the browser, with no SQL and no seeder edits.*

Rooms, exits, flags, and the canvas were done first and held up. The gap was a level up — the
**containers** those rooms live in — and `WorldPanel`, `ZonePanel`, and the per-flag primitives
for both scopes closed it.

- [x] Roles (player / builder / moderator / admin) + authorization policies **(moved up from Ops)**
- [x] `WorldMutation` path: enqueue → loop applies → persist → notify occupants
- [x] Request/response mutation pattern with loop ack (§7.3)
- [x] World / zone / room / exit CRUD API + `content_audit` on every write
- [x] **Room flag registry (§4.10):** typed accessors, room → zone → world resolution,
      unknown keys preserved and wrong-typed values treated as absent
- [x] `GET /api/builder/room-flags`; the room editor renders flags from it, inherited values
      shown greyed with their source
- [x] Graceful degradation for every broken state in §7.4 — with tests for each row
- [x] Advisory `/validate`: dangling exits, orphan rooms, unreachable areas, unknown flag keys,
      rooms that are PvP only by inheritance
- [x] Builder UI: world tree, room editor (full), zone panel (flags only)
- [x] **World editor** (`WorldPanel.tsx`) — name, description, sort order, flags, difficulty, and
      delete. There was no world editor of any kind before.
- [x] **World and zone delete are reachable.** Both routes and both `builderApi` functions
      existed with no caller anywhere, so a mistyped world was permanent from the browser.
- [x] **The zone editor renders the flag registry**, the way the room editor does, so a newly
      registered flag appears with no client change — and carries name, description, and level
      range. It was two literal buttons (`pvp`, `peaceful`) and nothing else.
- [x] **Per-flag world and zone primitives** (`SetWorldFlag`, `SetZoneFlag`, both `PUT
      …/flags/{flag}`). All three scopes now edit one key at a time. They were whole-map writes,
      so two builders editing one zone's flags at the same moment overwrote each other — silently,
      since the losing request carried a complete, valid, merely stale map.
- [x] ASCII grid painter with legend management
- [x] Zone canvas: auto-layout from the exit graph, drag to arrange, drag to link
- [x] Live push of edits to players standing in an edited room
- [x] **Walk-and-build (§7.6):** `dig` endpoint covering both materialize and dig cases
- [x] Reciprocal exits, provisional key generation, automatic `editor_x/y` placement
- [x] `flags.unfinished` on new rooms, `/unfinished` to-do list, hatched on the canvas
- [x] In-game builder commands: `dig`, `link`, `unlink`, `rtitle`, `rflag`, `goto`
- [x] Follow mode: builder panel re-targets the room the character walks into
- [x] Room rename rewrites inbound exit references in the same mutation
- [x] Dig rate limiting; builder-only gating with player fallback to "The way is blocked."
- [x] 232 .NET tests + 18 client tests

Notes from the build:

- **The loop normalises requests into primitives, and persistence replays that exact list.**
  A builder asks for `dig north`; the loop answers with `[UpsertRoom, SetExit, SetExit]` and
  hands the same list to the writer. Letting both sides interpret the request independently
  would work right up until they disagreed — and `dig` is where they would, because the loop
  picks the new room's key and nothing else can know it.
- **Reads come from Postgres, writes go through the loop.** Enumerating `WorldState` from a
  request thread is a real race, not a theoretical one: it is mutated with no locks, and the
  moment it would bite is a builder editing a busy world. Since every mutation persists before
  its HTTP call returns, the database is never more than one in-flight edit behind.
- **A failed persist reloads the world rather than leaving memory ahead of it.** The alternative
  was a 200 for an edit that evaporates at the next restart. The reload is blunt — every room,
  not just the failed one — but a failure here means Postgres is unreachable, not that the edit
  was bad, so bluntness costs nothing and correctness is restored.
- **In-game builder commands persist fire-and-forget, and that asymmetry is forced.** A command
  handler runs *on* the loop; enqueueing a mutation and awaiting it would wait for a pulse that
  cannot start until the current one ends. So `dig` typed at the command line queues its writes
  like a character save, and a failure is logged rather than reported. The builder panel, where
  anyone authoring seriously works, does not have that gap.
- **`peaceful` beating `pvp`, and absence beating both, is the whole safety argument.** The
  test that matters is `A_room_with_no_flags_at_all_is_not_pvp` — every other claim in §4.11
  rests on it.
- **EF scaffolded the `flags` migration with a default of `""`.** Postgres rejects an empty
  string as jsonb, so the generated migration would have failed on any database that already
  had a world in it. Hand-corrected to `{}`, with a comment saying why.
- **Test hosts no longer use the Windows Event Log provider.** Each test builds its own host in
  one process, and `EventLogLoggerProvider` wraps a handle the first host to be disposed closes
  for all of them; a later host logging a warning then threw `ObjectDisposedException` from
  inside `ILogger.Log`. It surfaced as a failure in the readiness test — the one most reliably
  logging a warning — with a stack trace pointing at logging rather than anything that test did.
- **Both background workers now swallow `OperationCanceledException`.** An exception escaping a
  `BackgroundService` is treated as a fault: the host logs Critical and stops. During a shutdown
  already under way, that Critical reaches logging providers midway through disposal.

## Phase 2a — Role administration ✅ **complete**
*Done when: an admin can make somebody a builder from inside the game, and a ban takes effect on
someone who is already connected.*

Small, and it came before Phase 3 because Phase 2 shipped a builder that needed SQL to reach
(§7.7).

- [x] `admin_audit` table + migration
- [x] `PATCH /api/admin/accounts/{username}/role`, `GET /api/admin/accounts?q=`, Admin policy
- [x] `AccountAdminQueue` + worker, mirroring the world write queue
- [x] In-game `promote`, `demote`, `whois` — admin-only, hidden from `help`, target may be offline
- [x] `Notify(sessionId, …)` inbound message so an async result reaches the admin as a `sys` event
- [x] `SetActorRole` so a promoted character's verbs work without relogging
- [x] `OnValidatePrincipal` revalidation on a ~60 s interval: refresh a changed role, reject a
      banned account — **this is what makes banning a connected player work at all**
- [x] Self-demotion refused; an installation must never be able to lose its last admin
- [x] README: replace the `UPDATE accounts` instruction with the command
- [x] 278 .NET tests

Notes from the build:

- **`AccountRole` is not a ladder, and pretending it is would be a privilege bug.** The numbers
  ascend, so `actor >= required` reads as obviously correct — and quietly makes every Moderator a
  builder. `AccountRoleExtensions.Satisfies` is the single definition; the HTTP policies and the
  in-game command table both derive from it, and a test asserts the two agree for all sixteen
  pairs. Two copies of an access rule is how they end up disagreeing.
- **The revalidation interval had to become configuration to be testable.** At a fixed 60 s, a
  test that promoted somebody and immediately checked their access would assert the cache rather
  than the behaviour — and would keep passing if revalidation were deleted. `Auth:Revalidation`
  `IntervalSeconds` is 0 in tests, and 0 is a legitimate production setting for a small server
  that would rather pay a read per request.
- **Roles are parsed in full, never by prefix.** `promote kael a` silently meaning Admin is the
  one place where the abbreviation convenience the rest of the parser is built on becomes a
  hazard. Typing the whole word *is* the confirmation step.
- **`Notify` is addressed by session, not by character.** An account may have several tabs open
  and only one of them typed the command; the reply belongs to the connection that asked.
- **A demoted builder is told, not silently stripped.** Verbs that stop working with no
  explanation read as the game breaking rather than as a decision somebody made.

## Phase 3 — Objects, inhabitants, and multipliers ✅ **complete**
*Done when: a zone's difficulty is a slider, and the same kobold is trivial in one zone and lethal in another.*

The engine half was correct from the start; the **slider** was what was missing, which is the
half the phase goal is written about. `MultiplierEditor` and its live preview sit on both the
world and the zone panel now, and mob stats and loot are authorable alongside them.

One deferred nice-to-have remains below, and it is not what the phase goal asks for.

- [x] Item templates + instances (weight and capacity limits deferred)
- [x] `get`, `drop`, `give`, `inventory`, `examine`, `destroy`; `put` for containers deferred
- [x] Equipment slots: `wear`, `wield`, `remove` (containers with depth limit deferred)
- [x] Mob templates, spawners, population maintenance
- [x] **Population is counted per spawner across the world, and mobs stay in their home zone.**
      Both found in playtesting rather than by a test, and they were the same bug wearing two
      faces: the sweep counted mobs standing in the spawner's own rooms, so anything that wandered
      out was replaced — and nothing stopped it wandering out of the zone either. A spawner set to
      three could fill a zone, and a crypt rat could stroll into the starting meadow carrying
      crypt numbers. Now `WorldState` indexes mobs by the spawner that made them, and a wandering
      mob turns back at the border unless its template sets `roams`.
- [x] **Multiplier resolution at spawn time** (§4.4), `spawn_multipliers` recorded per instance
- [x] World + zone multiplier storage and `world × zone` composition **in the engine and schema**
- [x] Builder: mob template, item template, and spawner editors (CRUD endpoints)
- [x] Builder: mob **behavior** editor — disposition, idle emotes with their cadence, whether it
      roams beyond its zone, shopkeeper and stock
- [x] Mob AI v1: idle emotes, room-to-room wandering, `sentinel` flag, fire-and-forget system tick
- [x] **Each idle emote keeps its own clock**, authored as a range. Every mob in the game used to
      share one hardcoded sixteen-pulse interval, so a shopkeeper's sales pitch and a rat's squeak
      arrived at the same rate — roughly fifteen times a minute — which is how a room full of
      atmosphere becomes a room you leave. From playtesting.
- [x] Ground items and mobs appear on the map with their icons
- [x] `emote` command for expressive actions — **including back to the person who typed it**,
      which it did not do. `;grins` broadcast to the room and echoed nothing, so in an empty room
      there was no way to tell a working emote from a swallowed one. From playtesting.
- [x] **Multipliers are authorable end to end.** The full slice landed: contract → primitive →
      applier → writer → `builderApi` → a Difficulty tab on both the world and zone editors.
      They were seed-and-SQL only, which made §7.5's "the reason the whole feature exists"
      the one dial nobody could turn.
- [x] **The multiplier preview panel exists.** *(Was checked off once while unbuilt.)* The
      endpoint had been correct since Phase 3 with no caller anywhere in `client/src`.
- [x] **Item spawners and multi-room spawners are authorable.** `SpawnerDialog` takes a kind
      toggle and both template lists, and a room multi-select; update sends `roomKeys`.
- [x] **Item template stats survive an edit.** `baseStats` is kept verbatim and only the five
      multiplier keys are written in place. Stats the form has no field for are listed read-only
      beneath it, so a builder can at least see the `"1d6"` that is there.
- [x] **Spawner removal is honest about scope.** The room list shows every spawner *containing*
      this room, and "Remove" deleted the whole thing while the confirmation said it would stop
      filling "this room" — tidying one room could empty twenty. It now removes this room from
      the set, and only deletes outright when this was the last room.
- [x] **The multiplier preview reported 40 health for everything.** `ResolveMobStats` read
      `BaseStats["health"]` as `value is int`, false for every jsonb-loaded template, so it fell
      through to its 40 fallback. A builder tuning the Strength dial was watching a number that
      was not their mob's. Now read through `JsonBag.Int32`.
- [x] **Loot table and `baseStats` editors for mob templates** (`MobStatsEditor`, `LootEditor`,
      both wired into `MobTemplateEditor`). Only `health` had a field, out of the ten keys the
      engine reads, so nine of a mob's stats were unauthorable from the browser. A test
      transcribes the engine's key list, so an editor that offers fewer fails.
- [x] **`Mob.State` reads through `JsonBag`.** It pattern-matched `is long` / `is true`, which is
      correct only while nothing reads or writes the `mobs` table. The schema and the
      `DbSet<Mob>` both exist, so the day mob persistence lands the sentinel flag and the emote
      timers would have gone quiet with no error — the same trap that had already cost three
      features. Disarmed before it fires rather than after.
- [x] Builder: *Respawn zone* button to apply live multiplier edits to existing mobs — deferred
      from this phase and built later. Takes down what a zone's mob spawners placed and refills
      every one to its target at once, bypassing `SpawnerSchedule` because the whole point is to
      see the numbers now. Hand-placed mobs and item spawners are left where they are.

## Phase 4 — Combat and progression ✅ **complete**
*Done when: you can kill something, loot its corpse, and level up — and the multipliers visibly matter.*

- [x] Combat state machine, since rebuilt onto per-combatant attack clocks (§2.3)
- [x] `kill`, `flee`, `consider`, auto-attack continuation
- [x] Damage model per §4.6, injectable RNG, full unit coverage of the formula
- [x] **Target validation gate (§4.11), separate from the damage formula:** `peaceful` forbids all
      combat, player-vs-player requires `pvp`, party members are never targets — the last of the
      three only stopped being a comment in 5.3, when there were parties to check against
- [x] PvP re-checked every round, so leaving a `pvp` room ends the fight; refusals are narrated
- [x] PvP kills recorded to the moderation log
- [x] **Death (§4.12):** no player corpse, no item loss, mob corpses unchanged
- [x] XP penalty as a fraction of the level band, floored at the threshold — never de-level;
      exempt below `Death:XpLossMinLevel` and on PvP deaths
- [x] `bind` in a `respawn`-flagged room; three-step respawn fall-through, stale bind cleared
- [x] Respawn at 25% Health / 0 Focus / 0 Stamina, out of combat; same path when link-dead
- [x] XP awards with zone `xp` multiplier, leveling, point spend
- [x] Regen tied to Vitality and rest state (`sleep`, `rest`, `stand`)
- [x] Aggressive mobs, assist behavior, target selection
- [x] `EquipmentResolver` wired into `CombatSystem` and the `stats` screen, so equipment
      multipliers reach the damage roll instead of decorating the character sheet
- [x] Mob damage: template stats win where declared, level-derived fallback where not
- [x] **Per-combatant attack timing.** Weapons carry an authored delay and verb; the off hand
      needs the `dual-wield` passive (Shade 3, Warden 5) to strike at all and `ambidextrous`
      (Shade 10, Warden 15) to strike on its own beat; mob templates carry an array of attacks,
      each on its own clock. A dead combatant no longer swings, and a cast in progress silences
      the swings that would otherwise interrupt it
- [x] **Builder-authored armour works.** `armorMultiplier` scales only the flat armour a piece
      already declares, and the item editor offered no way to declare any — so every authored
      piece resolved `0 × multiplier = 0`. The editor now writes `armorFlat`, `armorPercent`, and
      `defense` alongside the weapon stats, which is what the multiplier needed to act on.
      No armour *baseline* was added, and none should be: unarmed damage could take one (1–2)
      because the floor is a scratch, but a flat armour baseline of 10 would reduce nearly every
      hit in the game to 1. Unarmoured resolving to zero is the correct answer.

## Phase 5 — Depth

### 5.1e — Ability progression ✅ **complete**
- [x] **8 abilities per Path unlocking to level 20**, never more than four levels apart. It used
      to stop at level 6 for every Path, so levelling past it granted nothing.
- [x] **One `AbilityCatalogue`** feeds both the seeder and `AbilityProgression`. They were two
      hand-written lists and had drifted apart in both directions: four abilities were unlocked
      at level 6 with **no ability row behind them**, so the level-up granted something
      uncastable; and `warden.battle-fury`, `adept.weaken`, and `shade.fortify` were seeded but
      in no progression, so all of 5.2a's buffs and debuffs were **unlearnable**.
- [x] Ability rows **reconcile on every startup**, not once at seed time, so an existing database
      receives abilities added later. Seeding bailed whenever a world already existed.
- [x] **Parry is a passive**, not a castable self-heal (Warden 4, Shade 8). It rolls after the
      attack roll and before narration, so it only spends itself on a blow that would have landed.
      Mobs never parry — the chance comes from Path and level, which a mob has neither of.
- [x] **Debuffs were inverted.** `debuff.weaken` sets `IncomingDamageMultiplier`, which scales
      damage the target *takes*; every weaken was authored below 1.0 and so made its target
      25–45% **harder** to kill. `DebuffEffect` now also reads `outgoingMultiplier` — hardcoded
      to 1.0 before, so the effect could not express "deals less damage" at all.
- [x] **Ability rows reconcile in all three directions** — added, updated, and *purged*. The
      catalogue is authoritative, which is safe because abilities are the one content type with
      no builder UI and no foreign keys pointing at them. Without the purge, a renamed ability
      (`warden.slash` → `warden.kick`) left the old row in the table forever.
- [x] **`damage.overtime`** — the fifth executor, and the first that differs in *kind*: damage on
      a clock of its own rather than a number scaled at the moment of a swing. Shade's Ambush is a
      stacking bleed, Adept's Scorch a heavier burn, Hallow's Wither the long slow one. The
      tick lives in `CombatSystem.Tick` where the death, XP, and loot paths already are, so a
      bleed can land the killing blow — the consequence being that wounds only work during a
      fight, and fleeing stops the bleeding.
- [x] **`control.stun`** — the sixth, and the first that takes a *turn* away rather than changing
      a number. `PreventsActing` is checked in three places, because each gate is independent: the
      combat loop that swings, the `cast` command that starts spells, and the mob AI that emotes,
      wanders, and aggresses. The interrupt is driven by `ShouldInterrupt` reading the *state*, so
      any stun breaks a cast however it arrived. Duration is clamped in the effect (24 pulses) so
      an authored typo cannot remove someone from the game, and a catalogue test fails the build
      rather than letting the clamp apply silently. New `warden.shield-bash` at 9 — `warden.kick`
      stays instant damage.
- [x] **`control.root`** — the seventh, and the counterpart to the stun: it leaves the turn and
      closes the exit. What it denies is `flee`; ordinary movement is already refused mid-fight,
      so a root that only blocked walking would do nothing in the one situation it is cast in.
      Clamped to 40 pulses. `shade.shadowstep` became `shade.hamstring`, which decides whether a
      fight ends rather than how fast — something no damage number expressed.

### 5.1a–5.1d — Ability System ✅ **complete**
- [x] Ability system: cost, cooldown, cast time, targeting rules
- [x] `/cast <ability> [target]` command with validation
- [x] Per-Path ability trees with level-based unlocks
- [x] Extensible `IAbilityEffect` interface for effect resolution
- [x] `DamageEffect` and `HealEffect` with scaling and variance
- [x] Pulse-based cast resolution (250ms intervals)
- [x] Cast queue with interruption detection (movement, combat entry)
- [x] Cooldown tracking per (CharacterId, AbilityKey)
- [x] In-memory caching at game loop startup

### 5.2a — Buffs and debuffs ✅ **complete**
- [x] `ActiveEffect` with duration, `Refresh`/`Stack`/`Ignore` stacking rules, and max stacks
- [x] `BuffEffect` / `DebuffEffect` behind `IBuffEffect`, resolved through `EffectRegistry`
- [x] Outgoing and incoming damage multipliers read by `CombatSystem` each round
- [x] `EffectExpirySystem` sweeps expired effects off the pulse; effects are in-memory only
      and reset on restart, matching cooldowns and combat state
- [x] `buffs` command lists what is active and when it ends

### 5.2b — Quest engine ✅ **complete**
- [x] `quests` + `character_quests` tables, Active/Completed only, composite key
- [x] String keys for mobs and items rather than FKs, so a quest can be authored before its
      content exists (§7.4)
- [x] `talk <npc>`: offers when prerequisites are met, state-dependent dialogue for
      offer / in-progress / complete / turn-in
- [x] `give` hook: strict match on item and count, otherwise the NPC refuses and keeps nothing
- [x] Prerequisite chains, enforced on offer — the mechanism storylines are built from
- [x] **A chain step can start itself**, so handing in one leg opens the next with no second
      `talk`. Optional per quest, since a chain that should send the player somewhere wants the
      walk to matter.
      **Declared by the quest that gets started, not as a list of triggers on the one that starts
      it.** The obvious shape is `triggers: [next-quest]`, and it is the wrong one: it would be a
      *second* set of chain edges over the same quests, while `/zones/{zone}/storyline` draws
      `prerequisite_quest_keys` and nothing else. Two graphs, one of them invisible in the panel
      built to show chains, and nothing making them agree. `auto_start` on the follow-on quest
      keeps prerequisites the only answer to "what follows what" — and matches the direction
      content is authored in (§7.4): a builder writes this quest knowing what it follows, not the
      earlier one knowing what comes after it. Two quests behind the same step decide
      independently, so one can open by itself while the other still needs a conversation.
      It fires after the turn-in has marked its own quest Completed, which is what lets the
      ordinary prerequisite check do the work: a step with another prerequisite still open simply
      does not qualify. **Every rule a `talk` would apply is applied** — dormancy, and both repeat
      gates — because a quest that starts itself must not reach a state a player could not have
      reached by asking for it. It does not cascade: what it starts is Active, not Completed.
      Reads best when the same NPC takes the turn-in and gives the next one, which is the case
      worth having: the old man takes the beer, hands over the glass, and asks for it back in one
      exchange.
- [x] Repeatable quests with a `TimesCompleted` counter. **Repeatable is a property of the chain,
      not of the leg** — which only became visible once a chain existed to test it on.
      Two gates, both on the re-offer only, since a first offer has no history to consult:
      - **Nothing downstream may still be Active.** A player who had finished *A Fresh Drink* and
        was carrying the glass could take the errand again while *The Empty Glass* was open, so
        the first leg reset to Active behind the second and the journal described a state the
        story cannot be in — the beer not yet delivered, the glass already in hand. Downstream is
        walked transitively rather than stored, for the reason dormancy is derived (§7.4):
        prerequisites are edited live and a cached answer would be wrong the moment a builder
        inserted a step. The player's two ways to clear it are the two they already have — finish
        it, or `abandon` it.
      - **Every prerequisite must have run again.** Otherwise a player who had completed the whole
        chain could take the *second* leg straight from its giver, arriving with no glass and no
        way to get one, because taking the first leg is then blocked by this one being active.
        Recoverable by abandoning, but a chain should be re-entered at its head. `TimesCompleted`
        already counts the runs, so the comparison needs no new state: after one pass both legs
        sit at 1 and the second is not re-offered; run the head again and it is at 2, which is
        what opens the second.
      A quest that is **not** repeatable is unaffected by either gate — finishing the rest of the
      story must not quietly reopen the parts meant to happen once.
- [x] `quests` journal and `quest <name>` detail. **`quest <name>` did not reach the detail view
      for four months** — a verb matches on any prefix of its name and `Find` takes the first
      definition that matches, so with `quests` registered ahead of it and asking for only three
      characters, `"quests".StartsWith("quest")` sent every `quest fresh` to the journal.
      `QuestDetail` had no reachable input at all: dead from the day it was written, advertised in
      `help`, and symptomless except that the output was wrong. The §12 lesson again, in the one
      form a call-site audit misses — the code *was* called, by nothing a player could type.
      `quest` is registered first now, and bare `quest` falls through to the journal so no
      abbreviation is wasted on a "Which quest?" prompt.
      The other prefix pairs were safe by accident of their numbers rather than by design:
      `whois` demands five characters so `who` cannot reach it, `stats` demands five so `stat`
      cannot. That is now a test over the whole table rather than a property nobody had noticed
      holding.
- [x] **`abandon <name>`** — a way out of a quest, which a chain turns from a convenience into a
      necessity: prerequisites mean an abandoned leg blocks everything behind it, and before this
      the journal listed it Active for ever while the giver answered with its in-progress line.
      A soft-lock built out of dialogue rather than code, which is the hardest kind to see.
      It **removes the state** rather than marking it, because §6 already spells "not started" as
      the absence of a row — so no `QuestStatus.Abandoned`, no migration, and nothing else has to
      learn a third state. The delete reaches storage, since forgetting it in memory alone would
      have the quest reappear Active at the next restart. The one exception is a repeatable quest
      already finished at least once: deleting that row would erase `TimesCompleted`, so it
      reverts to Completed and keeps the count.
      **Items are left alone.** Taking them back would destroy player property on a verb typed by
      mistake, and the item may have come from an earlier leg that is no longer repeatable —
      which would make the chain permanently unfinishable rather than merely abandoned. Nothing
      is stranded either: only `destroy` and `sell` refuse quest items, so a held one can be put
      down and picked back up.
- [x] `CharacterQuestSaveQueue` so progress survives restart
- [x] Builder API: quest CRUD, reachability (`GET /quests/{key}/reachability`), and the
      storyline graph with cycle detection (`GET /zones/{zoneKey}/storyline`)
- [x] **Builder UI: a Quests tab** (`QuestsTab`, `QuestEditor`, `QuestCreateDialog`,
      `StorylinePanel`, `ReachabilityPanel`). This line was previously checked off naming a
      `QuestEditor.tsx` that was never written, while `builderApi`'s five quest functions had zero
      callers — the §12 lesson repeating, and the worst version of it, because the plan claimed a
      whole authoring surface that did not exist. Three columns rather than the templates' two:
      a quest is not isolated the way a template is, and a chain is invisible from inside any
      quest in it.
      Every mob and item reference is picked from a real template, because a typo produces a
      *dormant* quest — one that reads perfectly in the journal, is offered by nobody, and reports
      no error anywhere. Prerequisites stay free text, since a chain is routinely authored
      backwards (§7.4) and the storyline panel is what reports a key that never turns up.
      Reachability warnings render in the editor, where §10 says they have to be: an unobtainable
      required item fails silently in play, so the quest reads correctly and the player just
      wanders.
      Two things the API shape forced, both worth knowing: **create needs a zone, giver, and
      turn-in**, so quests get their own create dialog rather than the shared key-and-name one;
      and **a quest key is one segment**, not the dotted composite a room uses.

### 5.2c — Shops and currency ✅ **complete**
- [x] `buy` / `sell` / `list` against a mob flagged `shopkeeper`
- [x] Priced through `itemValue`; sellback at a configurable percentage
- [x] Gold on the character, persisted through `CharacterSaveQueue`
- [x] Shopkeepers and their stock are authorable in the builder (`MobBehaviorEditor.tsx`), with
      the stock picked from real item templates rather than typed as keys
- [x] Shop coverage: `ShopCommandTests` (engine) exercises list / buy / sell against behavior in
      the shape storage returns it

### 5.2d — Gaps in the above, found by audit ✅ **complete**
These were the parts of 5.2 that had been checked off in spirit but were not in the code. Every
one failed quietly, which is why they needed listing rather than assuming — and is the reason
§12's rule now reads "before checking a box, name the test or the call site."

- [x] **The behavior bag was unreadable.** `Behavior` is `Dictionary<string, object>` stored as
      jsonb, so every value arrives as a `JsonElement`. `ShopCommands` tested `is bool` and
      `is List<object>`, and `MobAiSystem` tested `is List<object>` — all false for any template
      that had round-tripped, which is every template outside a unit test. **Shopkeeper
      detection, shop stock, and idle emotes were dead in the running game**; only aggression
      survived, by luck. Now read through `JsonBag` / `MobBehavior`, the counterpart to
      `StatReader` for the non-numeric bags.
- [x] **`buy` could never complete.** It looked its zone up with `RoomKey.Zone` (the bare
      segment) where `FindZone` wants the qualified `world.zone`, so every purchase fell through
      to "the shop is temporarily unavailable".
- [x] **Quest rewards ignore zone multipliers.** Now resolved through `Multipliers.Resolve` the
      way combat resolves `mob.ResolvedXp`. Two adjacent bugs fixed with it: the reward-item
      `Reply` sat inside the spawn loop (a count of 3 announced itself three times), and a
      missing template or zone dropped the reward in silence.
- [x] **`/reachability` cannot fail.** Rewritten to walk loot tables, item spawners, and other
      quests' rewards, and to check giver/turn-in mobs exist and are spawned.
- [x] **Non-combatant mobs.** `type: "npc"` mobs can neither attack nor be attacked, so a quest
      giver or shopkeeper cannot be killed into a soft-lock (§7.4). Authorable in the builder.
- [x] **`questItem` is written as well as read.** A toggle on the item template editor sets
      `IsQuestItem`, and `ItemSpawner` stamps the flag onto every instance — the one path shop
      stock, quest rewards, the spawner sweep, and combat loot all pass through. Both readers had
      been correct and both dead, so neither rule had ever fired in play. Two call sites that
      hand-built an `ItemInstance` from a spawned one now use the spawned instance directly;
      they would otherwise have dropped the stamp.
- [x] **Dormant quests.** A quest whose giver, turn-in, or required item no longer exists stops
      being offered, and an in-progress copy stays in the journal marked *unavailable*. Dormancy
      is **derived, not stored** — no `QuestStatus.Unavailable`, no migration, and restoring the
      content revives the quest with no repair pass. No player row is ever written.
- [x] **`cast` could not target a mob.** Target resolution searched players only, so every
      offensive ability resolved to no target against the things you actually fight — and because
      cost and cooldown are spent *before* the target is resolved, it charged in full, started the
      cooldown, and narrated "takes effect!". Now matches mobs through `NameMatch`, falls back to
      the current combat target for harmful abilities and to self for helpful ones, and refuses
      before charging when nothing matches.
- [x] **Every ability was on cooldown at boot.** `GetAbilityCooldown` returned `0` for "never
      cast", which is a real pulse — so for the first `CooldownPulses` of server uptime the whole
      spellbook was refused. Returns `long?` now.
- [x] **The Hallow could not heal anyone.** Every supportive ability on the support Path was
      `TargetingType.Self`. They are `SingleTarget` now, and a helpful ability cast with no target
      named still lands on the caster.
- [x] **`TargetingType.Aoe` resolves.** `AbilitySystem` gathers a target *list*, filtered per
      target as §4.11 requires: a harmful cast takes every mob that may be fought — skipping
      non-combatants — plus other players only where the room resolves `pvp`, and nothing at all
      in a `peaceful` room; a helpful one takes the caster and the people standing with them and
      leaves the mobs alone. One cost and one cooldown however many it lands on, and a cast that
      gathers nobody is refused before either is spent. Two abilities declare it: `adept.firestorm`
      (18) and `hallow.benediction` (18).
      **Party membership now overrides both flags** (5.3): a harmful area effect skips the
      caster's group even where `pvp` is set — the one room it mattered in — and a helpful one
      prefers the group over the room, falling back to the room when the caster is ungrouped.
- [x] **Which way an ability points is declared by its executor**, `IAbilityEffect.IsHarmful`,
      rather than by a hardcoded list of two effect keys in the command layer. That list predated
      five of the seven executors and classified all of them as helpful, so `cast scorch` with no
      target named set the caster on fire. It also decides which set an area effect gathers.

- [x] **The tick systems no longer query the database once per entity per tick.** `MobAiSystem`
      called `GetByKeyAsync` per mob every 16 pulses (4 s) and `SpawnerSystem` did the same per
      spawner per kind every 60 (15 s), each opening a fresh `DbContext` for a single-row
      `AsNoTracking` read that nothing memoized — a world with twenty mobs made twenty round-trips
      every four seconds with nobody logged in. Both now read `MobTemplateCache` /
      `ItemTemplateCache`, which already held every template, load at boot, and are kept live by
      the applier; only the command paths and the Phase 4 systems read them, because these two
      systems predate them.
      **The fallback is gated on `IsLoaded`, not on a miss** — a deliberate departure from the
      note this item started as. A per-miss fallback looks safer and behaves worse: a mob or
      spawner whose template a builder deleted misses forever, so it would reissue the doomed
      query on every sweep for the life of the process, which is the pathology being removed in
      the one case where it can never pay off. An unloaded cache is a different failure, and
      reading through to the repository for it is what stops a host that never called `LoadAsync`
      from silently having no mob AI at all.
      Behaviour is identical either way, which is why nothing surfaced this until the SQL log was
      read — so the tests count repository calls rather than assert on outcomes.

- [x] **The spawner rules themselves are cached too.** The item above moved the *templates* out of
      the database and left the sweep still opening a `DbContext` for
      `SELECT … FROM spawners` every 15 seconds — the last per-tick query in the engine, and the
      one still visible in the SQL log afterwards. `SpawnerCache` is the same shape as the template
      caches, keyed by `Guid` rather than by key, loaded at boot and kept live by the applier:
      `ApplyUpsertSpawner` / `ApplyDeleteSpawner` had been no-ops that forwarded the change to
      persistence and nothing else, so this is also what makes a spawner saved in the builder take
      effect on the next sweep instead of at the next restart.
      **Gated on `IsLoaded` for the same reason, but the miss question does not arise** — the sweep
      wants the whole set, so there is no key to miss on and the only read-through case is a host
      that never loaded the cache.
      The sweep **copies** the values rather than enumerating them live. It is fire-and-forget, so
      a template read that *does* fall through to the database parks the rest of the loop on the
      thread pool, and a builder saving a spawner on the loop thread meanwhile would invalidate the
      enumerator underneath it. One list of a few dozen references every 15 seconds is not the cost
      being avoided here.
      Unchanged: deleting a spawner stops it being enforced but does not despawn what it already
      placed.

### 5.2f — Threat, and the gate every hostile action goes through

- [x] **Every kind of damage counts on the hate list.** `GetTopHater` reads cumulative damage, so
      the list was always a damage meter that reorders itself — but only landed melee swings fed
      it. Ability damage went from the executor straight into `Vitals.Health`, and damage-over-time
      ticks straight in from the combat loop. That inverted the design: **the Adept, the Path built
      to deal the most damage in the game, was the only one that could never pull a mob off
      anybody**, and the Shade's Ambush was worth no threat at all. Damage is measured as the
      target's health before and after, since the executors return `void`; a tick credits
      `ActiveEffect.SourceEntityId`, because the caster may have left and the bleed keeps working.
- [x] **Hurting a mob with an ability starts the fight**, the way swinging at it does. An opening
      Bolt used to be free — the mob took the damage and stood there, nothing having put it in
      combat. Engages without retargeting, so an area effect cannot swing the caster's own weapon
      round to the last thing the flames touched.
- [x] **`taunt` (Warden 8) and `provoke` (Shade 12)** — the first thing in the game that *writes*
      threat instead of earning it. Sets the caster above the current top by a fraction of the
      target's max health (0.30 / 0.18), which is a **lead, not a lock**: the list is still a
      damage meter afterwards, so whoever was displaced climbs back by out-damaging the taunter.
      Expressed as a fraction because threat grows without bound over a fight — a flat lead would
      be decisive in the first ten seconds and beneath notice five minutes in, and would mean
      nothing consistent between a rat and a dragon.
- [x] **One §4.11 gate for every hostile action** (`HostileActionGate`). `kill` refused a
      `peaceful` room, a non-combatant, and an unsanctioned duel; **`cast` checked none of the
      three**, so a Bolt worked in a safe room and an Adept could kill the shopkeeper handing out
      the zone's quests. Area effects had grown a third copy of the same rules. All three answer
      through one place now — which is what makes taunt safe, since it is a way to start a fight
      with something you never attacked.
- [x] **A malformed entity ID can no longer kill the game loop.** One in a hate list became the
      top hater and threw on the next tick when nothing could resolve its room. Effect sources
      survive a jsonb round trip and outlive the cast that set them, so `EntityId.IsWellFormed`
      guards the door. Found by a test, not in production.
- [x] **You can name yourself as a target.** The player search is `OthersIn`, which excludes the
      caster, so a Hallow typing their own name at their own heal — the obvious thing to type —
      got "You don't see 'Bram' here." while standing there.

### 5.2e — Deferred from the original 5.2 list
- [→] `noRecall` refuses teleports out — **moved to 5.3**, where the `recall` command it is
      waiting for now sits. The flag is registered with no reader, which is not a bug on its own:
      §4.10 says a flag with no reader is dead weight, and this one is dead weight *until the
      verb exists*. Listing it apart from that verb was what made it look like an oversight.
- [x] Path respec — **decided against** (Q3, §4.5). Paths are fixed at creation; rerolling is the
      respec. Nothing to build, which is the point: this line closes rather than defers.
- [→] Pets and charmed mobs inheriting their owner's §4.11 permissions — **moved to §13**. There
      is no pet system and none is planned for a numbered phase, so this had been sitting in a
      phase checklist as work that could never start.

### 5.3 — Communication and travel
- [x] **Parties, session-scoped.** A `PartyRegistry` in `WorldState`, beside combat and active
      effects, with no table behind it: a party describes who is in the world right now, and
      persisting one would raise a question — what a party whose members are all offline means —
      that nothing in §4.11 or the split needs answered. `WorldState.Remove` is the one door out
      of the world, so party cleanup lives there rather than at the four call sites that reach it.
      Going link-dead deliberately does *not* clean up: §3.6 leaves that character standing in the
      room, and a group that dissolved over ten seconds of bad wifi would be worse than one that
      waited.
      `group` with subcommands (invite, accept, decline, leave, kick, disband), six members,
      invitations that expire after a minute, and leadership that passes when the leader walks
      out. A party of one dissolves itself — otherwise it is a state you cannot tell from being
      grouped, and it silently changes how a helpful area effect gathers.
- [x] **The three approximations waiting on parties are gone**, each of which carried a comment
      saying so. `AreaTargets` filtered by the `pvp` flag as a stand-in for membership;
      `TargetValidator`'s summary claimed *"party members are never valid targets"* with nothing
      enforcing it; and the XP split had nothing to split between.
- [x] **Party XP and gold split**, evenly, remainder to whoever landed the blow, among members
      **standing in the room where it died**. A member two zones away shares nothing — a group
      that could farm by scattering would make the split an exploit rather than a convenience.
      **No group bonus**: four people killing one mob earn exactly what one earns, so grouping is
      a social choice rather than an efficient one. That may be the wrong call, but it is one
      number in `RewardShare` and inventing a multiplier before anyone has played in a group would
      be balancing against a guess.
- [x] **`tell`, `reply`, a world channel, and a party channel.** `tell` and `gtell` cross rooms,
      which is what makes them different from `say`. `chat off` silences the world channel in
      *both* directions — a channel you can shout into while ignoring the replies is not one
      anybody else wants to share. The short forms were chosen so no older verb lost its
      abbreviation: `t` is still talk, `r` still rest, `c` still consider, `g` still get.
- [x] **`AbilityValidator` deleted.** A third copy of the §4.11 rules with no production callers,
      which applied the hostile check to *every* single-target ability — so anyone who wired it up
      would have found that healing another player was refused everywhere except a `pvp` room. Its
      cost check, targeting check, and self-target rule are all enforced on the live path already.
- [x] **A second world needs no portal concept — an ordinary exit already crosses worlds.**
      This line used to read *"a second world reachable by portal"*, which invented a mechanism
      for something the exit system does already. `RoomExit.ToRoomKey` is a fully-qualified
      `world.zone.room`; `ApplyLink` compares nothing but whether the source room exists;
      movement resolves the key and moves; `/validate` looks its targets up globally, so a
      cross-world exit is not reported as dangling; the zone canvas skips edges to rooms it does
      not hold, so the layout degrades rather than breaking; and Add Exit already takes a free
      `world.zone.room`.
      So this is **content, not code** — build the world, link a room to it. Whether the door is
      described as a portal, a ship, or a staircase is prose.
- [x] **`recall`, and the `noRecall` flag finally has one reader.** `Travel.Refuse` is that
      reader, and it is the seam any future teleport goes through — a flag becomes a lie when a
      second travel verb lands later and forgets to ask.
      Recall returns you to the bind point `bind` already sets and death already uses, rather than
      a second configured room that could drift from it; which also means `bind` now matters while
      you are alive. It is free and uncooled, which sounds generous until you notice what it is:
      dying on purpose without the XP loss. What stops it being an escape hatch is that it refuses
      mid-fight, so the fight you would want out of is the one it will not take you out of.
      Deliberately not used by the builder's `goto`, which is documented as ignoring exits — a
      builder held in place by the content they are editing would have to walk out to fix it.
- [→] Teleport *as a spell* — not built, and not a gap. A destination is a `world.zone.room`
      like any other, so a teleport effect is a parameter, not a new kind of link: nothing about
      travelling needs to know whether the target is in this world. When one is wanted it is an
      executor reading a room key and calling `Travel`, which is why that seam exists now rather
      than being invented alongside the spell.

### 5.4 — From play: the shop dial and the pack listing ✅ **complete**

Three notes off `PlayTestingNotes.md`, all of them about content a builder cannot express or text a
player cannot read at a glance. None is a bug; each is a system that shipped in its simplest form
and has now been played long enough to want one more turn of the handle.

- [x] **A shopkeeper carries a markup** (§4.13). One `markup` key in the behavior bag, `0.1`
      meaning 1.1×, rounded up to the next whole gold with a floor of one gold over base. It moves
      `list` and `buy` and leaves sellback alone, so *expensive to buy from* and *pays well* stay
      two different things a builder can set independently. `ShopPricing` in Domain rather than in
      the command, beside `Multipliers.Resolve`: it is arithmetic over two numbers with no world in
      it, and the rounding rule wants a unit test rather than a shop.
      **`buy` prices from the shop the item was matched in**, not from the first shopkeeper in the
      room. `list` already walked every shop and `buy` already searched every stock list; with one
      price in the world that was invisible, and with a markup it would put the smith's rates on
      the baker's bread.
- [x] **The markup is authorable**, in `MobBehaviorEditor` next to the stock it applies to, with
      the asking price shown against each stocked item and the base value beside it — the same
      argument the multiplier preview (§7.5) makes, that a builder tuning a number should see what
      it does before saving rather than by walking into the shop.
      **The preview reimplements the formula, and the interesting part is where it cannot.**
      `decimal` is why the server's version is three lines: in binary floating point 100 × 1.1 is
      110.00000000000001, so a ceiling in TypeScript charges a gold of representation error. The
      client rounds to six places before the ceiling; both sides carry the same worked cases in
      their tests, which is the only thing keeping a preview honest that is a copy by nature.
- [x] **Quest items are tagged `(q)`** rather than `(quest)` (§4.14). Same rule, shorter mark.
- [x] **Identical items collapse to one line with a count** — `stone (x3)`, and `(x2 q)` when
      they are also quest items. Display only; every verb that takes an item still takes one item,
      which is asserted rather than assumed: `drop stone` against three stones drops one.
- [x] **Two playtest plans**, because both halves of this are presentation and the suite can only
      assert that a substring appears (PLAYTEST.md). `shop-markup` walks a player through a price
      list, a refusal, a purchase, and a sale back, so the *same figure* has to survive four
      sentences built in four places — the shape that drifts. `the-pack` buys three of one thing
      and reads the listing. The markup plan needs a shopkeeper that charges over base, which is
      content rather than commands: it is documented beside the test dummy for a person to build,
      since a plan cannot author a shop until the `world:` fixture block has something behind it.


---

## Phase 6 — the half that has landed

Phase 6 is in progress; PLAN.md §8 keeps the open items. These are the ones that shipped, moved
here for the same reason the phases above were.

- [x] **Admin commands the loop can answer itself: `teleport`, `inspect`, `kick`, `shutdown`.**
      In `AdminWorldCommands`, deliberately apart from `AdminCommands`: those touch the *account*
      store, which §2.1 forbids the loop, so each hands off to a queue and is answered later.
      These are about the world, which the loop owns outright. Mixing the two would make it
      impossible to tell from the file which kind you were reading.
      - `teleport <name>` fetches a player to you — `goto` already went the other way, and
        fetching is what you want when answering *"I am stuck"*. It ignores `noRecall`, roots, and
        combat on purpose: being held by content is the usual reason to need it, and a tool the
        content could veto is no use in the case it exists for.
      - `inspect` answers what the room description cannot — which spawner is responsible for a
        mob still being here, which zone's multipliers produced its numbers, what is on its hate
        list. Both of the questions the last round of playtesting raised.
        *Shipped as `stat`, renamed later:* four characters put it in front of the player-facing
        `stats`, so `stat` reached an admin verb and a player asking for their own combat sheet
        was told the verb does not exist. `inspect` is the better name for it anyway.
      - `kick` hands the removal back to the loop rather than doing it in the handler: leaving the
        world saves, closes the channel, and redraws the room, and the second copy of that list is
        the one that goes stale. Says something in the room, too — a character vanishing with no
        explanation reads as a bug.
      - `shutdown <minutes|now|cancel>` with warnings at nine milestones, from thirty minutes down
        to five seconds. **From playtesting.** Progress is already safe (§11); what a warning
        protects is the half hour someone spent walking to a boss, which no save gives back.
        Minutes, not seconds, because that is the unit the decision is made in. Immediate is
        spelled `now` rather than `0`, so the destructive case is a word typed on purpose.
        `IShutdownSignal` keeps the Engine ignorant of being hosted; the Server implements it over
        `IHostApplicationLifetime`, which is what makes the stop orderly rather than abrupt.
- [x] **`ban` / `unban`, `mute` / `unmute`, and `set`.** The first four outlive the session, so
      they go through `IAccountAdminQueue` beside `promote`; `set` is world-side and answers
      immediately.
      - **A ban evicts.** Cookie revalidation (§7.7) refuses the *next* request, but an SSE stream
        is one long-lived request that was authorised before the ban existed — so a ban without
        `EvictAccount` leaves the banned player in the world until they choose to leave. By
        account rather than by session, or a second tab keeps playing.
      - **A mute is a time, not a flag.** `accounts.muted_until`, nullable, so it lifts itself
        with no sweep whose only job is tidiness. The duration is required rather than defaulted:
        a mute with no stated end is one somebody has to remember to lift, and a forgotten one is
        indistinguishable from a ban nobody meant to apply.
      - **A mute covers every verb that carries words to another player** — `say`, `emote`,
        `tell`, `reply`, `chat`, `gtell` — through one `RefusedForMute` gate. Anything less is a
        mute in name only: a silenced player still has a private channel to five group members.
        It *refuses* rather than swallowing, because a player talking to a room that cannot hear
        them is a crueller punishment than the one chosen, and looks like a bug besides.
      - **`set` takes a closed list of fields**, not reflection over `Character`. Reflection would
        expose every field added afterwards, including ones where writing directly corrupts
        something else — setting `RoomKey` without moving the actor leaves a character indexed in
        a room they are not in. It reports the old value beside the new, because the reason to
        reach for it is that a number was wrong.
      (`goto` shipped with Phase 2; `promote` / `demote` / `whois` with Phase 2a)
- [x] **Rate limiting per caller.** Three policies, because the three surfaces differ: commands
      partitioned by *character* (the thing the single-threaded loop spends its budget on, so an
      account with three characters legitimately costs three characters' worth), builder by
      account and deliberately looser, auth by remote address and tight — the only one defending
      against a stranger. A 429 carries `Retry-After`, since a client that retries immediately
      turns one breach into the tight loop being limited.
      The SSE stream is deliberately *not* limited: one long-lived request per character, where a
      limiter would never fire on honest use and would break a session that reconnected twice.
      **Known weakness, named rather than hidden:** behind the nginx front end every caller shares
      the proxy's address, so the auth limit is a site-wide cap until forwarded headers are
      honoured. The numbers are configuration (`RateLimits:*`) for exactly this reason. Honouring
      `X-Forwarded-For` safely needs a trusted-proxy list, which is a deployment input this repo
      does not have — so it is a deployment note in `Program.cs`, not a silent default.
      (`DigThrottle` predates this and still covers `dig` on its own, guarding against a held key
      carving forty rooms rather than against load.)
- [x] **Instrumented, with no exporter** — `EngineMetrics`, six instruments on a meter named
      `Muwbta.Engine`: pulse duration, pulses over budget, command latency, commands handled,
      active sessions, rooms loaded. All `System.Diagnostics.Metrics` primitives from the base
      library, so **nothing was added to the dependency graph**.
      The line is drawn there deliberately: instrumentation has to live in the code, but where it
      is sent is a deployment decision and can be made later. A deployment that wants these
      shipped adds an OpenTelemetry exporter pointed at the meter name and changes no code.
      **Every pulse is recorded, not only the slow ones.** The watchdog logs a line when a pulse
      runs over budget, which answers *did that happen* and cannot answer *is it getting worse* —
      a log has no distribution, so one bad pulse and a p99 that has been creeping up for a week
      look identical.
      Command latency is stamped at **gateway acceptance**, not when the loop picks the message
      up: the queue wait is the interesting part of the §11 target, and measuring from the pick-up
      would hide exactly the delay worth seeing.
- [x] **World export/import (JSON) for moving content between environments.**
      `GET /api/builder/export` and `POST /api/builder/import`, builder-authorised like every
      other content route. Carries the same eight tables `tools/export-content.sql` covers, for
      the same reasons — accounts, characters, item instances, and the audit tables are player
      data and history, and a content restore that resurrected deleted characters would be a bug.
      Abilities stay out because `ReconcileAbilitiesAsync` rebuilds them from `AbilityCatalogue`
      on every startup, so importing them would write rows the next boot only has to correct.
      - **A scoped export is closed over its references, not merely filtered.** Rooms, spawners,
        and quests belong to a zone, so scoping those is a `where`. Templates are global — so a
        zone bundle that filtered them the same way would import cleanly and then *spawn nothing*.
        `?zone=` therefore also carries every template its spawners place, every mob and item its
        quests name, and every item those mobs drop. That last hop is the one worth naming: a
        required quest item usually arrives through a loot table rather than a spawner, and
        without it the target environment gets a quest nobody can finish — §10's silent failure,
        shipped by the tool meant to move content safely. The world above a zone travels with it
        too, because multipliers resolve through the world (§4.4), so a zone imported without its
        world would have *wrong* numbers rather than missing ones.
      - **Every entity goes through `WorldEditor`, one primitive at a time.** Nothing writes to
        Postgres directly. That keeps the loop the single writer (§2.1), makes imported content
        visible to players already standing in the rooms, and leaves a `content_audit` row per
        entity — for a change this size, *"who replaced the crypt"* has to be one query.
      - **An import is a merge, not a mirror.** Keys in the bundle are upserted; keys this
        environment has and the bundle does not are left alone. There is deliberately no
        "replace" mode that deletes the difference: §10 already carries *a bad live edit is
        visible instantly with no rollback* as a standing risk, and a mode whose failure is
        deleting a zone somebody else authored is that risk with a bigger blast radius and no
        better recovery. Removing content stays a deliberate, per-entity act.
      - **Spawners travel with their id.** A spawner has no content key to collide on, so an
        import that minted a fresh one would double the population of every zone it touched on
        the second run — invisible in the editor, obvious only in play.
      - **Unknown flags are carried, not dropped.** The builder API drops a flag the registry
        does not know on the way in, deliberately (§4.10). The importer does the opposite,
        because this is transport rather than authoring: the flag already exists in another
        environment, and quietly rewriting content in transit between two builds is worse than
        keeping a key nothing reads.
      - **Known weakness, named rather than hidden: an import is not atomic.** One mutation per
        entity is one loop round trip and one transaction, so a failure part way through leaves
        what came before it applied. Making the whole bundle atomic needs a batch primitive the
        loop does not have. The mitigation is cheaper and honest — `?dryRun=true` reports every
        collision and dangling reference while changing nothing, a partial import answers **207**
        rather than 200, and the report names exactly which entities did not land.
      - Everything else is advisory, as `/validate` is (§7.4): a quest whose giver is in another
        zone, an exit pointing somewhere not here yet. Refusing those would make the
        zone-at-a-time workflow the scoped export exists for impossible. The **format version is
        the one hard refusal** — a bundle this build cannot read would otherwise apply the fields
        that happened to match and silently drop the rest, which is what a version number is for.

---

## What §12 carried while it was open

Two items sat in PLAN.md §12 as *the next step* long enough to accumulate their own reasoning.
Both are built; the record of what they were and why the shape changed is kept here.

The items this section carried last, kept as the record of what they were and why the shape
changed:

1. **Parties.** Three approximations were waiting on them, each carrying a comment saying so:
   `AreaTargets` leaned on the `pvp` flag as a stand-in for membership, `TargetValidator`'s summary
   listed *"party members are never valid targets"* as a rule nothing enforced, and the XP split
   had nothing to split between. All three are now real. Session-scoped was the call — no schema,
   no save queue, and no question about what a party of offline characters means.
   The one thing the build surfaced that the plan had not: **`AbilityValidator` was a fourth copy
   of §4.11 with no callers**, and it applied the hostile check to helpful abilities, so wiring it
   up would have made healing another player impossible outside a `pvp` room. Deleted rather than
   corrected — the live path already enforces every rule it held.

2. **Mob attacks carry effects.** ~~Every one of the seven executors is a player-only tool~~ — the
   asymmetry was live rather than theoretical: a Warden's Shield Bash (9) takes a boss off its
   feet for three seconds and the boss had no answer of any kind.
   This used to read *"mobs cannot cast"*, which overstates it and invites a build big enough to
   defer forever. **The receiving side is already finished**: `ActiveEffect`, `world.ApplyEffect`,
   `IsStunned` gating both the cast command and the combat loop, `PreventsEscape` gating `flee`.
   A player can already receive all seven. What is missing is a way for a mob to *emit* one.
   So the shape is an optional `effectKey` and params on `MobAttack`, applied where its damage
   already lands — no `CastJob`, no cast bar, no mob focus pool, no target-selection changes, and
   no migration, since `attacks` is already `jsonb`. It reuses the effect registry and the
   per-attack timers that already let a wolf bite every second and rake every three.
   *A departure from the traditional mob spellcaster, deliberately: a mob's abilities are things
   it does with its attacks rather than a spellbook it works through.*
   Shipped as `MobAttack.EffectKey` + `EffectParams`, applied in `CombatSystem.ApplyRider` where
   the swing's damage already lands — so the effect inherits the miss chance, the parry, and the
   death check for free, and a stun you dodged does not stun you. It lands only on a survivor.
   An unknown key is ignored at runtime (absence is the safe value, as with flags) and **refused**
   at the builder API, where a typo is still cheap to fix. The editor offers only harmful effects:
   a rider hits whoever the attack hit, so a helpful one would mean a mob mending the player it
   just struck.
