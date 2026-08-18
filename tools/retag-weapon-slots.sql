-- Weapon slots: either hand, both hands, or the main hand only.
--
-- Run AFTER the `ItemSlotList` migration, which adds `slots` and `is_two_handed` and converts
-- every existing `slot` into a one-element list. This file only retags the weapons whose shape
-- says they belong somewhere else; everything the migration wrote is already correct.
--
--     docker exec -i dikuweb-postgres psql -U dikuweb -d dikuweb < tools/retag-weapon-slots.sql
--
-- Idempotent: it sets values rather than toggling them, so running it twice is running it once.
--
-- **Restart the web process afterwards.** ItemTemplateCache is loaded at startup and does not
-- notice a direct SQL write, so until it reloads the game goes on believing every weapon is
-- main-hand only. Same trap as the ability retune (RUNBOOK.md §5).
--
-- The alternative to this file is importing `build/the-reaches.json`, which carries the same
-- retag plus everything else authored since the last import. Prefer that when the world is
-- otherwise up to date; prefer this when it is not and you only want the slots.

BEGIN;

-- Light one-handers: blades, cleavers, a hand axe, a dredging hook, and the Shade's quiet knife
-- at every act. These are what make Dual Wield reachable at all - before this there was no weapon
-- in the game that could enter an off hand.
UPDATE item_templates
   SET slots = '["MainHand", "OffHand"]'::jsonb,
       is_two_handed = false
 WHERE key IN (
    'ossara-short-blade',
    'ossara-hand-axe',
    'grask-claim-cleaver',
    'grask-dredge-hook',
    'azhen-gate-stylus',
    'nemhal-keening-blade',
    'unlit-long-blade',
    'epic-shade-1',
    'epic-shade-2',
    'epic-shade-3',
    'epic-shade-4',
    'epic-shade-5'
 );

-- Hafted and long. The shop line only: an epic reward that arrives forbidding a shield is a
-- restriction the player never chose, and the Warden's oathmaul is the one it would land on.
UPDATE item_templates
   SET slots = '["MainHand"]'::jsonb,
       is_two_handed = true
 WHERE key IN (
    'ossara-walking-staff',
    'grask-long-pick',
    'azhen-counterweight-maul',
    'nemhal-standing-maul',
    'unlit-standing-hammer'
 );

-- What it should say: 12 either-hand, 5 two-handed.
SELECT
    count(*) FILTER (WHERE slots = '["MainHand", "OffHand"]'::jsonb) AS either_hand,
    count(*) FILTER (WHERE is_two_handed)                            AS two_handed
  FROM item_templates;

COMMIT;
