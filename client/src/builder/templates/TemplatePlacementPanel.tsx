import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import {
  builderApi,
  type PlacementMob,
  type TemplatePlacement,
} from '../../net/builderApi'
import { toMobsPath, toQuestsPath, toRoomPath } from '../routes'

interface Props {
  kind: 'mob' | 'item'
  templateKey: string | null
  /** Bumped by the editor after a save, so a renamed or retuned template redraws. */
  revision: number
}

/** `0.25` reads as `25%`, and a certain drop reads as `always` rather than `100%`. */
function chanceLabel(chance: number | null): string {
  if (chance === null) return ''
  if (chance >= 1) return 'always'
  return `${Math.round(chance * 100)}%`
}

/**
 * A mob an item comes from, with the one thing neither editor can show: whether anything places
 * that mob at all.
 */
function MobRow({ mob, onSelect }: { mob: PlacementMob; onSelect: (key: string) => void }) {
  return (
    <li>
      <button type="button" className="link" onClick={() => onSelect(mob.key)} title={mob.key}>
        {mob.name || mob.key}
      </button>
      {mob.chance !== null && <span className="dim"> · {chanceLabel(mob.chance)}</span>}
      {!mob.placed && <span className="bad"> · no spawner places it</span>}
    </li>
  )
}

/**
 * Where a template actually exists in the world (PLAN.md §7.9).
 *
 * <b>Every relationship shown here is stored on the other side.</b> A spawner names its template,
 * a loot row names its item, a quest names its reward — so from inside a template's own editor all
 * of them are invisible, and the question a builder actually has about the thing they are editing
 * is "where is this, and does anyone ever meet it". Answering it meant opening the World tab and
 * checking rooms one at a time.
 *
 * Modelled on the quest chain: a rail beside the editor, plain indented text rather than a canvas,
 * and every row a link to the thing it names — because the next thing a builder does after finding
 * out where a mob spawns is go there.
 */
export function TemplatePlacementPanel({ kind, templateKey, revision }: Props) {
  const navigate = useNavigate()
  const [placement, setPlacement] = useState<TemplatePlacement | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!templateKey) {
      setPlacement(null)
      return
    }

    let cancelled = false
    setError(null)
    const load = kind === 'mob' ? builderApi.mobPlacement : builderApi.itemPlacement

    void load(templateKey)
      .then((loaded) => {
        if (!cancelled) setPlacement(loaded)
      })
      .catch((e) => {
        if (!cancelled) {
          setPlacement(null)
          setError(e instanceof Error ? e.message : 'Could not load placement.')
        }
      })

    return () => {
      cancelled = true
    }
  }, [kind, templateKey, revision])

  if (!templateKey) {
    return <p className="dim detail">Select a template to see where it is placed.</p>
  }

  if (error) return <p className="bad">{error}</p>
  if (!placement) return <p className="dim detail">Loading…</p>

  const { spawners, droppedBy, soldBy, quests } = placement
  const nowhere =
    spawners.length === 0 && droppedBy.length === 0 && soldBy.length === 0 && quests.length === 0

  if (nowhere) {
    return (
      <p className="dim detail">
        {kind === 'mob'
          ? 'No spawner places this mob, so nobody will ever meet it.'
          : 'Nothing spawns, drops, sells, or rewards this item, so nobody can ever get one.'}
      </p>
    )
  }

  return (
    <div className="section-body placement">
      {spawners.length > 0 && (
        <section>
          <h4>Spawners</h4>
          <ul className="placement-list">
            {spawners.map((spawner) => (
              <li key={spawner.id}>
                <div className="placement-head">
                  {spawner.zoneName || spawner.zoneKey}
                  <span className="dim">
                    {' · '}
                    {spawner.targetCount}× · respawn {spawner.respawnSeconds}s
                    {/* The level a placement produces, not the level the template was authored
                        at - a zone's dials move it, and that is the number that decides whether
                        killing this teaches anyone anything (§4.7). */}
                    {kind === 'mob' && spawner.fightsAtLevel > 0 &&
                      ` · fights at level ${spawner.fightsAtLevel}`}
                    {/* Null unless this placement renames the template (§4.8), so it only
                        appears on the rows where the word differs from the heading. */}
                    {kind === 'mob' && spawner.spawnsAs ? ` · as ${spawner.spawnsAs}` : ''}
                  </span>
                </div>

                <ul className="placement-rooms">
                  {spawner.rooms.map((room) => (
                    <li key={room.key}>
                      {room.title === null ? (
                        /* A spawner pointing at a room that no longer exists is allowed (§7.4)
                           and is a finding, not a row to hide: it places nothing, anywhere. */
                        <>
                          <code>{room.key}</code>
                          <span className="bad"> · no such room</span>
                        </>
                      ) : (
                        <button
                          type="button"
                          className="link"
                          title={room.key}
                          onClick={() => navigate(toRoomPath(room.key))}
                        >
                          {room.title}
                        </button>
                      )}
                    </li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </section>
      )}

      {droppedBy.length > 0 && (
        <section>
          <h4>Dropped by</h4>
          <ul className="placement-list">
            {droppedBy.map((mob) => (
              <MobRow key={mob.key} mob={mob} onSelect={(key) => navigate(toMobsPath(key))} />
            ))}
          </ul>
        </section>
      )}

      {soldBy.length > 0 && (
        <section>
          <h4>Sold by</h4>
          <ul className="placement-list">
            {soldBy.map((mob) => (
              <MobRow key={mob.key} mob={mob} onSelect={(key) => navigate(toMobsPath(key))} />
            ))}
          </ul>
        </section>
      )}

      {quests.length > 0 && (
        <section>
          <h4>Quests</h4>
          <ul className="placement-list">
            {quests.map((quest) => (
              <li key={`${quest.key}-${quest.role}`}>
                <button
                  type="button"
                  className="link"
                  title={quest.key}
                  onClick={() => navigate(toQuestsPath(quest.key))}
                >
                  {quest.name || quest.key}
                </button>
                <span className="dim">
                  {' · '}
                  {quest.role === 'reward' ? 'reward' : 'required for'}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* Only on the item panel, and only when it is the whole answer. An item that exists solely
          as a reward is a real thing to author - it is how an epic stays unbuyable (§4.13) - but
          it is also what a forgotten loot table looks like, and the two are worth telling apart
          before a player is the one who notices. */}
      {kind === 'item' &&
        spawners.length === 0 &&
        droppedBy.length === 0 &&
        soldBy.length === 0 &&
        quests.length > 0 && (
          <p className="dim detail">
            A quest reward and nothing else — this item has no source in the world itself.
          </p>
        )}
    </div>
  )
}
