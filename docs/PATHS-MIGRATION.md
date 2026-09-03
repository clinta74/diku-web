# Database-Driven Character Paths — Migration Plan

> Status: planned, not scheduled. Written 2026-08-22. This is a design/evaluation document
> for a future build; no code has been changed.

## Context

Player paths (Warden, Adept, Temper, Hallow) are a compiled enum (`CharacterPath`) with all per-path data hardcoded in C# switch expressions: starting vitals, stat growth, passives + tuning numbers, and focus regen rate. This blocks the real goal — a themed world (e.g. cyberpunk) offering its own path set ("Soldier" instead of "Warden") authored by builders like any other content. It also means renaming a path is a hand-written data migration (see `20260820003718_RenameShadePathToTemper.cs`), and a removed path would brick every character on it.

**Settled decisions:**
1. **String-keyed `paths` table** replaces the enum as identity — flexible path count, builders create paths like abilities.
2. **Global path catalog with an active path set** — the active `game_configurations` row lists the paths available in the deployment. The set gates character creation **and** which existing characters are playable.
3. **Locked characters, no path switching, no fallback path.** A character is bound to its path for life. If its path leaves the active set, the character shows **greyed out / unplayable** in the character list (with its path named, so the player knows why) and cannot enter the world — fully reversible by re-adding the path to the set. A path row can **never be deleted** from the catalog while characters reference it; retirement = removal from the active set. Adding paths to a set is always safe.

This model needs no pathless fallback path, no path-switch operation, and no attribute-recompute policy. `Character.Path` stays init-only and `CharacterSnapshot` is unchanged.

## Current state (verified 2026-08-22)

- Enum persisted as **name** on `characters.path` (varchar(16)), as **ordinal int** on `abilities.path`, as **jsonb name arrays** on `item_templates.paths` / `quests.paths`.
- Hardcoded seams: `Vitals.StartingFor` (`Vitals.cs:18`), `PathGrowth.For` (`PathGrowth.cs:54`), passives + `ParryChance`/`OffHandDamageShare` (`AbilityProgression.cs:85-204`), `RegenCalculator.FocusRateFor` (`RegenCalculator.cs:33`).
- Known abilities are **derived** from `abilities` rows by (Path, UnlockLevel) — never persisted per character. Ability keys must be prefixed `<pathlower>.` (load-bearing in `AbilityLookup`).
- `Character.Path` is init-only; `CharacterSnapshot` (save queue) does not carry Path (and won't need to).
- Client hardcodes the four names twice: `AuthScreen.tsx:6-15` (picker) and `builderApi.ts:359` (`CHARACTER_PATHS` TS union).
- Content pipeline precedent to clone: ability builder endpoints → `WorldChange` → `WorldMutationApplier` + `WorldWriter`; `WorldBundle` formatVersion 15; `AbilityCache`; `ReconcileAbilitiesAsync` (insert-if-absent). Postgres is the only source of truth; `content/*.json` are export artifacts.
- `GameLoop.HandleEnter` already refuses/relocates on missing referents (missing-room relocation) — precedent for the entry gate.

## Design

### 1. `PathDefinition` entity + `paths` table

New `src/Muwbta.Domain/Characters/PathDefinition.cs`:

- `Key` (slug, `^[a-z][a-z0-9-]*$`, max 32 — e.g. `warden`, `soldier`), `Name` (display), `Blurb` (picker text — moves `PATH_BLURBS` server-side), `Description`, `SortOrder`
- `StartingVitals` (jsonb owned, reuse `Vitals`), `GrowthPerLevel` (jsonb, existing `StatGrowth` record), `FocusRegenRate` (double, replaces `FocusRateFor`)
- `Passives`: jsonb list of `PassiveGrant(UnlockLevel, PassiveKey, Params)` — **behavior stays in code** keyed by `PassiveKeys`; the **numbers move to data**:
  - `passive.parry` → `{chance}` (Warden 0.20, Temper 0.12)
  - `passive.dual-wield` → `{fullShare, masteryLevel}` (Temper 1.00, Warden 0.80; 40)
  - `passive.ambidextrous` → no params
  - Ramp formula (half-share at unlock → full at mastery) stays in code. Unknown passive keys/params = validator **warning**, code-side defaults apply.

New `PathConfiguration` in Persistence (scalars as columns, vitals/growth/passives jsonb — matches existing conventions), DbSet on `MuwbtaDbContext`.

### 2. Schema migrations (ordinal map: 0→warden, 1→adept, 2→temper, 3→hallow)

**Migration 1 — CreatePathsTable** (Phase A): create table; seed the 4 canonical rows **in-migration** (raw SQL, like RenameShadePathToTemper) from today's constants — so Migration 2's FK conversions can't run against an empty table. `StarterWorldSeeder.ReconcilePathsAsync` added (insert-if-absent, never updates — builder retunes survive restarts).

**Migration 2 — ConvertPathColumnsToKeys** (Phase B):
- `abilities`: add `path_key varchar(32)`, backfill from ordinal, NOT NULL, drop `path`; replace `ix_abilities_path_unlock_level` with `ix_abilities_path_key_unlock_level`; FK → `paths.key` **ON DELETE RESTRICT**.
- `characters.path`: widen to 32, lowercase names → slugs; FK → `paths.key` **ON DELETE RESTRICT**. The "refuse delete always" rule is thereby a database guarantee, not just an endpoint guard — note this includes soft-deleted characters (they hold the FK too), which is fine: a path with any history is retired via the set, not deleted.
- `item_templates.paths` / `quests.paths`: jsonb name arrays → slug arrays (CASE rewrite). No FK on jsonb — app-level warnings only; an unknown slug is inert.
- **Ability key prefix rule stays**: key must start with `<path_key>.` — existing keys already conform, slug is already lowercase.

**Migration 3 — ConfigurationPathSet** (Phase E): add `path_keys jsonb NOT NULL DEFAULT '[]'` to `game_configurations` (empty = all catalog paths available — preserves current behavior before any config opts in).

The enum itself is deleted from Domain code in Phase C (code, not schema).

### 3. Domain/engine refactor

- `Vitals.StartingFor` / `PathGrowth.For` deleted → callers read `pathDef.StartingVitals` / `.GrowthPerLevel`. `RegenCalculator`, `VitalCalculator`, `CharacterProgression`, `AbilityProgression` take `PathDefinition` instead of the enum; switch expressions collapse into `Params` lookups.
- `Character.Path` (enum) → `PathKey` (string, **stays init-only** — no operation ever rewrites it). `CharacterConfiguration` drops `HasConversion<string>()`, widens to 32. `CharacterSnapshot`/save worker untouched.
- New **`PathCache`** (mirrors `AbilityCache`: `LoadAsync(IPathRepository)`, `Put`/`Remove`), loaded at loop start **before** `AbilityCache` (abilities validate against paths). Loop resolves each character's `PathDefinition` at `HandleEnter` and holds the reference on the runtime character state.
- **Entry gate** (in `HandleEnter`, beside the missing-room relocation): if the character's path key is not in the active set (or unresolvable — shouldn't happen with the FK, but SQL surgery exists), refuse entry with a clear message. The server-side enter endpoint applies the same check so the client gets a proper error rather than a dropped session.

### 4. Locked characters (path not in active set)

- `GET /api/characters` returns every living character with an `available: bool` (path key ∈ active set, or set empty) and `pathName`. **No filtering server-side beyond the flag** — the client renders unavailable characters greyed out with "«Path» is not part of this world" and disables Enter. Reversible: re-add the path to the set (or activate the old config) and they light back up.
- World entry (`POST /api/game/{id}/enter` + loop `HandleEnter`) refuses unavailable characters — the flag is advisory UI; the gate is authoritative.
- A character online when its path leaves the active set (config activation is a live WorldChange): recommend **grace until logout** — they finish their session, can't re-enter afterwards. Avoids mid-combat forced ejection logic; config activation is rare and admin-driven. (Open question below if immediate ejection is preferred.)

### 5. Per-configuration path set + creation

- `GameConfiguration.PathKeys` rides along `UpsertGameConfiguration`/`ActivateGameConfiguration` WorldChange records → `EngineOptions.PathKeys` → `LoadActiveConfigurationAsync` (exactly the StartingRoom/WelcomeMessage pattern); `WorldMutationApplier` updates it on activation.
- `CharacterEndpoints` creation: replace `Enum.TryParse` with paths-table lookup; 400 unless the path exists and is in the active set (or set empty). Vitals from `pathDef.StartingVitals`; attributes stay `AttributeSet.Baseline`.
- New `GET /api/characters/paths` → `[{key, name, blurb, sortOrder}]` filtered to the active set, for the picker. (Verified: the picker is post-login; no anonymous endpoint needed.)
- Config CRUD endpoints/DTOs and client config editor gain the path-set field (multi-select fetched from `/api/builder/paths`); validator **warns** when a listed key doesn't exist yet (config may be authored before a bundle import lands).

### 6. Builder surface

- `/api/builder/paths` CRUD cloned from the ability endpoints (BuilderEndpoints.cs:932-1090): DTOs in `BuilderContracts` (nullable-field PATCH semantics; passives as `[{unlockLevel, passiveKey, params}]`), reads via `BuilderQueries`, writes via `WorldEditor.ApplyAsync` → new **`UpsertPath` / `DeletePath`** WorldChange records → `WorldMutationApplier` (PathCache.Put/Remove + re-resolve live holders so retunes apply immediately) + `WorldWriter` (transaction + content_audit) + `BuilderChangeFeed` SSE.
- New **`PathValidator`** (refuse-on-error like ability saves):
  - Errors: bad slug; non-positive vitals/regen; passive unlock levels outside 1..50; **delete while abilities reference it** ("move or delete its abilities first"); **delete while any characters reference it** (no force override — FK RESTRICT backs this; retirement is set-removal).
  - Warnings only: growth total ≠ 6 (all four current paths total 6, but themed worlds may want deliberately unbalanced paths — don't hard-enforce a balance convention), unknown passive key/param names, path in the active set with no abilities, duplicate passive keys.
- `AbilityValidator`/`BundleValidator`: per-path set rules (`ValidateSet`) keyed off the paths table instead of `Enum.GetValues`; prefix rule per slug; `CooldownGroup` identity becomes `(PathKey, number)`; the bundle's "all four paths present" gate becomes per-path (run set-level rules for each path whose abilities the bundle fully carries).
- Bundle: `Paths` array added to `WorldBundle`, `CurrentFormatVersion` 15→**16** (weak bump); `WorldImporter` applies paths **before abilities and configurations**; `WorldExporter` + `content/paths.json`; `tools/merge-bundles.cs`, `check-bundle.cs`, export tools updated.
- Client builder: new `PathsTab`/`PathEditor` (cloned from ability editor, passives sub-list with params); `builderApi.ts` `CHARACTER_PATHS` union → dynamic strings + `fetchPaths()`; `ItemTemplateEditor`/`QuestEditor`/`AbilityCreateDialog` pickers fetch from API; `BuilderContracts` ability/item/quest path fields become strings (drop `NullableEnumConverter<CharacterPath>`).

### 7. Game client

- `AuthScreen.tsx`: delete hardcoded `PATHS`/`PATH_BLURBS`; picker fetches `/api/characters/paths`; submits slug; character list renders `available: false` entries greyed with reason.
- Payloads carry **both** slug (`path`, stable id) and `pathName` (display) — HUD (`VitalsPayload`, PlayerView.cs:424), character list, who/party/consider/abilities headings render the display name.

## Files to touch (by area)

- **Domain**: `CharacterPath.cs` (delete, Phase C), `PathDefinition.cs` (new), `Character.cs`, `Vitals.cs`, `PathGrowth.cs`, `AbilityProgression.cs`, `Ability.cs` (Path→PathKey), `RegenCalculator.cs`, `VitalCalculator.cs`, `CharacterProgression.cs`, `ItemRules.cs`
- **Persistence**: `PathConfiguration.cs` (new), `AbilityConfiguration.cs`, `CharacterConfiguration.cs`, `ItemTemplateConfiguration.cs`, `QuestConfiguration.cs`, `GameConfigurationConfiguration.cs`, `MuwbtaDbContext.cs`, 3 migrations, `StarterWorldSeeder.cs`, `EfPathRepository` (new)
- **Engine**: `PathCache` (new), `EngineContracts.cs` (EngineOptions.PathKeys), `GameLoop.cs` (cache load order, entry gate), `CombatSystem.cs` (:396, :409, :668, :821, :1428-1441), `RegenSystem.cs`, `AbilityLookup.cs`, `LevelUpUnlocks.cs`, `Mutations/WorldChange.cs` (UpsertPath/DeletePath, UpsertAbility.Path→string, config records), `WorldMutationApplier`, `PlayerView.cs` (:179, :280, :403, :424), `CommandRegistry.cs` (:358, :1026-1088, :1151, :1352), `AbilityCommands.cs`, `CombatCommands.cs`, `PartyCommands.cs`, `AdminWorldCommands.cs`, `QuestCommands.cs`
- **Server**: `CharacterEndpoints.cs` (creation, list `available` flag, `/paths`), `GameEndpoints.cs` (enter gate), `BuilderEndpoints.cs`, `BuilderContracts.cs`, `BuilderQueries`, `WorldEditor`/`WorldWriter`/`WorldImporter`/`WorldExporter`, `WorldBundle.cs`, `AbilityValidator.cs`, `PathValidator.cs` (new), `BundleValidator`, `Program.cs` (`LoadActiveConfigurationAsync`)
- **Client**: `AuthScreen.tsx`, `net/api.ts`, HUD, `builder/builderApi.ts`, `PathsTab`/`PathEditor` (new), `ItemTemplateEditor`, `QuestEditor`, `AbilityCreateDialog`, config editor, `pathPicker.test.tsx`
- **Tools/tests/docs**: `tools/describe-abilities.cs` (**fixes pre-existing `CharacterPath.Shade` compile error**), `tools/Muwbta.Playtest/PlanRunner.cs:125`, bundle tools; `AbilityContentTests.cs:30` (4-path array → read seeded paths), `PassiveProgressionTests`, `RegenCalculatorTests` (enum iteration → seeded-path fixtures), new PathValidator/PathCache/entry-gate tests; `ABILITIES.md`, `PLAN.md`, `content/paths.json`

## Phasing (each phase leaves the game working)

- **A — Table + seed + dual-read.** Migration 1, entity/config/repo/cache; domain functions gain `PathDefinition` overloads resolved from the enum's lowercase name (enum overloads remain as delegating shims). *Verify*: `dotnet test`; boot; characterization test asserting old == new outputs for all 4 paths × levels 1–50.
- **B — Persistence conversion.** Migration 2; `PathKey` columns + FKs; validators re-pointed. *Verify*: migrate a DB copy, diff path counts before/after; builder export→import→export round-trip of `content/abilities.json` diffs clean.
- **C — Retire the enum.** Delete `CharacterPath` + shims; display names in commands/payloads; fix `describe-abilities.cs`. *Verify*: grep `CharacterPath` → only migrations; full tests; playtest tool.
- **D — Builder UI + bundle.** WorldChanges, PathValidator, endpoints, PathsTab, bundle v16 + import order + exporter. *Verify*: create "soldier" path + one `soldier.` ability, export→wipe-import→export diff clean; live-retune warden parry chance and confirm in combat without restart; attempt delete of warden → refused (abilities + characters).
- **E — Path set + picker + locked characters.** Migration 3, config field end-to-end, `GET /api/characters/paths`, creation validation, `available` flag + greyed list UI, enter gate. *Verify*: activate a config listing only "soldier" → picker shows only Soldier, warden creation → 400, existing Warden character greys out and enter → refused; re-add warden to set → playable again; empty set → everything available.

## Resolved during planning

- **Import deletion**: `WorldImporter` is a merge, never a mirror — a bundle swap cannot delete paths out from under characters. Retired paths accumulate in the catalog; the active set is what hides them. This harmonizes with "refuse delete always."
- **No pathless path / no switch**: an earlier draft used a fallback "pathless" path plus an admin switch operation; superseded by the locked-set model — characters are permanently bound to their path and simply lock when it's unavailable.

## Open questions / risks

1. **Online character when its path leaves the active set**: recommend grace-until-logout (refuse next entry) over mid-session ejection. Confirm at build time.
2. **Growth total ≠ 6 as warning only** — a builder can deliberately unbalance a themed world.
3. **Live retune blast radius**: `PathCache.Put` re-resolves all current holders and recomputes vitals maxima immediately — confirm vs. next-login.
4. **Cooldown group identity** moves from `(enum, number)` to `(pathKey, number)` — verify no persisted cooldown state encodes the ordinal.
5. **FK RESTRICT includes soft-deleted characters**: a path with any character history is effectively permanent in the catalog (retire via set). Acceptable given "refuse always"; note it in builder UI copy ("this path has N characters, including deleted ones").
6. **varchar 32 slugs** — confirm no UI truncation assumptions.
