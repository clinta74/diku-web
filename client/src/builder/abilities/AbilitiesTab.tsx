import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { builderApi, type Ability } from '../../net/builderApi'
import { toAbilitiesPath } from '../routes'
import { AbilityEditor } from './AbilityEditor'
import { AbilityCreateDialog } from './AbilityCreateDialog'

const PATH_ORDER = ['Warden', 'Adept', 'Shade', 'Hallow'] as const

/**
 * The Abilities tab: every ability grouped by Path, in unlock order, with an editor beside it.
 *
 * Grouped by Path and ordered by level rather than listed alphabetically, because the thing a
 * builder is nearly always checking is the *shape of a progression* — where the gaps are, what a
 * level-1 character has, whether two things land on the same level. An alphabetical list answers
 * none of those and the tree components elsewhere in the builder cannot express them.
 *
 * Loads its own data rather than going through `BuilderData`. That provider fetches worlds, mobs,
 * items, and quests on every builder open, and abilities are needed by exactly this tab — adding
 * them there would put a request on the critical path of every other screen to save one here.
 */
export function AbilitiesTab() {
  const navigate = useNavigate()
  const { abilityKey } = useParams()
  const [abilities, setAbilities] = useState<Ability[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  // A failed load and an empty list must not look the same. This was `.catch(() => [])`, copied
  // from the shared data provider, and it renders a 401, a 404, or a server that has not been
  // restarted as "this game has no abilities" - with nothing on screen to suggest otherwise. That
  // is the silent-failure shape the ability validator exists to prevent, reintroduced in the
  // screen built to display it.
  const refresh = async () => {
    try {
      setAbilities(await builderApi.abilities())
      setFailed(null)
    } catch (e) {
      setFailed(e instanceof Error ? e.message : 'Could not load abilities.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void refresh()
  }, [])

  const selectedKey = abilityKey ?? null

  return (
    <div className="builder-columns builder-columns-2">
      <aside className="builder-col">
        <div className="tree-head">
          <h3>Abilities</h3>
          <button type="button" onClick={() => setCreating(true)}>
            + New
          </button>
        </div>

        {loading && <p className="dim">Loading…</p>}

        {!loading && failed && (
          <p className="bad">
            {failed} If the server was started before the Abilities tab existed, restart it.
          </p>
        )}

        {!loading && !failed && abilities.length === 0 && (
          <p className="dim">
            No abilities in the database. A fresh install seeds them on first boot from the starter
            catalogue.
          </p>
        )}

        {!loading &&
          !failed &&
          PATH_ORDER.map((path) => {
            const forPath = abilities
              .filter((a) => a.path === path)
              .sort((a, b) => a.unlockLevel - b.unlockLevel)

            if (forPath.length === 0) return null

            return (
              <section key={path} className="ability-group">
                <h4 className="ability-group-head">{path}</h4>
                <ul className="ability-list">
                  {forPath.map((ability) => {
                    // An error means the ability does not work at all, so it outranks the level
                    // badge for attention. Warnings are quieter on purpose - they are usually
                    // about progression shape, which is a judgement rather than a fault.
                    const errors = ability.problems.filter((p) => p.severity === 'Error').length
                    const warnings = ability.problems.length - errors

                    return (
                      <li key={ability.key}>
                        <button
                          type="button"
                          className={ability.key === selectedKey ? 'tree-item selected' : 'tree-item'}
                          onClick={() => navigate(toAbilitiesPath(ability.key))}
                        >
                          <span className="ability-level">{ability.unlockLevel}</span>
                          <span className="ability-name">{ability.name}</span>
                          {errors > 0 && (
                            <span className="ability-flag bad" title="This ability will not work">
                              ✕
                            </span>
                          )}
                          {errors === 0 && warnings > 0 && (
                            <span className="ability-flag warn" title="Worth a look">
                              !
                            </span>
                          )}
                        </button>
                      </li>
                    )
                  })}
                </ul>
              </section>
            )
          })}
      </aside>

      <main className="builder-col">
        {selectedKey ? (
          <AbilityEditor
            key={selectedKey}
            abilityKey={selectedKey}
            onChanged={() => void refresh()}
            onDeleted={() => {
              void refresh()
              navigate(toAbilitiesPath())
            }}
          />
        ) : (
          <p className="dim">Pick an ability, or add one.</p>
        )}
      </main>

      <AbilityCreateDialog
        open={creating}
        onOpenChange={setCreating}
        onCreated={(key) => {
          void refresh()
          navigate(toAbilitiesPath(key))
        }}
      />
    </div>
  )
}
