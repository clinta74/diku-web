import type { ItemTemplate } from '../../net/builderApi'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import { TemplatePicker } from '../templates/TemplatePicker'
import { DISPOSITIONS, type BehaviorDraft, type Disposition } from './behavior'

interface Props {
  draft: BehaviorDraft
  itemTemplates: ItemTemplate[]
  onChange: (next: BehaviorDraft) => void
}

/**
 * Edits the parts of a mob's behavior the engine reads (PLAN.md §9.4).
 *
 * Before this the whole bag was passed through a save untouched, so a shopkeeper or an NPC could
 * only be created by writing JSON into the database by hand - which meant §5.2c shops had no
 * authoring path at all.
 */
export function MobBehaviorEditor({ draft, itemTemplates, onChange }: Props) {
  const set = (patch: Partial<BehaviorDraft>) => onChange({ ...draft, ...patch })

  const editEmote = (index: number, value: string) =>
    set({ emotes: draft.emotes.map((e, i) => (i === index ? value : e)) })

  const stocked = new Set(draft.sells)
  const unstocked = itemTemplates.filter((t) => !stocked.has(t.key))

  return (
    <fieldset className="behavior-editor">
      <legend>Behavior</legend>

      <Field
        label="Disposition"
        hint={DISPOSITIONS.find((d) => d.value === draft.disposition)?.hint}
      >
        <Select
          value={draft.disposition}
          onChange={(v) => set({ disposition: v as Disposition })}
        >
          {DISPOSITIONS.map((d) => (
            <option key={d.value} value={d.value}>
              {d.label}
            </option>
          ))}
        </Select>
      </Field>

      {draft.disposition === 'npc' && (
        <p className="dim">
          Quest givers, quest turn-ins, and shopkeepers should all be NPCs. A killable quest
          giver strands anyone mid-quest until it respawns. To stop one wandering off, set{' '}
          <strong>Sentinel</strong> on its spawner.
        </p>
      )}

      <div className="emote-list">
        <span className="field-label">Idle emotes</span>
        <p className="dim">
          Shown every few seconds as “{'{name}'} {draft.emotes[0] || 'snarls'}.” Leave empty and
          this mob stands in silence.
        </p>

        {draft.emotes.map((emote, index) => (
          <div className="field-row" key={index}>
            <input
              value={emote}
              placeholder="scratches at the floor"
              onChange={(e) => editEmote(index, e.target.value)}
            />
            <button
              type="button"
              className="danger"
              onClick={() => set({ emotes: draft.emotes.filter((_, i) => i !== index) })}
            >
              Remove
            </button>
          </div>
        ))}

        <button type="button" onClick={() => set({ emotes: [...draft.emotes, ''] })}>
          Add emote
        </button>
      </div>

      <label className="field-check">
        <input
          type="checkbox"
          checked={draft.roams}
          onChange={(e) => set({ roams: e.target.checked })}
        />
        Wanders beyond its home zone
      </label>

      <p className="dim">
        {draft.roams
          ? 'This mob will follow exits into neighbouring zones, carrying the stats it spawned with — which came from its own zone’s multipliers.'
          : 'Off by default: this mob wanders freely inside the zone it spawned in and turns back at the border. Rooms flagged noMob still refuse it either way.'}
      </p>

      <label className="field-check">
        <input
          type="checkbox"
          checked={draft.shopkeeper}
          onChange={(e) => set({ shopkeeper: e.target.checked })}
        />
        Keeps a shop (players can <code>list</code>, <code>buy</code>, and <code>sell</code> here)
      </label>

      {draft.shopkeeper && (
        <div className="shop-stock">
          <span className="field-label">Sells</span>
          <p className="dim">
            Priced at each item’s base value. Selling back pays half. Stock is unlimited — buying
            spawns a fresh instance rather than moving one.
          </p>

          {draft.sells.length === 0 && <p className="dim">Nothing stocked yet.</p>}

          {draft.sells.map((itemKey, index) => {
            const template = itemTemplates.find((t) => t.key === itemKey)
            return (
              <div className="field-row" key={`${itemKey}-${index}`}>
                <span className="stock-row">
                  {template ? (
                    <>
                      {template.name} <span className="dim">· {template.baseValue} gold</span>
                    </>
                  ) : (
                    /* The template was deleted out from under the shop. Say so here rather than
                       silently dropping the row, which would hide the broken reference. */
                    <span className="bad">{itemKey} — no such item template</span>
                  )}
                </span>
                <button
                  type="button"
                  className="danger"
                  onClick={() => set({ sells: draft.sells.filter((_, i) => i !== index) })}
                >
                  Remove
                </button>
              </div>
            )
          })}

          <Field label="Add an item" hint="Type to filter by key or name.">
            <TemplatePicker
              value=""
              options={unstocked.map((t) => ({
                key: t.key,
                name: `${t.name || t.key} (${t.baseValue} gold)`,
              }))}
              onChange={(key) => {
                // Only a real template is added: this field is an "add" action rather than a
                // value, so a half-typed key must not become a stock row.
                if (unstocked.some((t) => t.key === key)) set({ sells: [...draft.sells, key] })
              }}
            />
          </Field>
        </div>
      )}
    </fieldset>
  )
}
