import { useState } from 'react'
import { BuilderColumns } from '../BuilderColumns'
import { Button } from '../../ui/Button'
import { useNavigate, useParams } from 'react-router'
import { useBuilderData } from '../BuilderData'
import { toQuestsPath } from '../routes'
import { QuestCreateDialog } from './QuestCreateDialog'
import { QuestEditor } from './QuestEditor'
import { StorylinePanel } from './StorylinePanel'

/**
 * The Quests tab (PLAN.md §4.9, §5.2b).
 *
 * Three columns rather than the two the Mobs and Items tabs use, because a quest is not an
 * isolated thing the way a template is: its prerequisites are the only storyline mechanism there
 * is, and a chain is invisible from inside any single quest in it. The right rail is the chain
 * the selected quest sits in.
 */
export function QuestsTab() {
  const navigate = useNavigate()
  const { questKey } = useParams()
  const { quests, refreshQuests } = useBuilderData()

  const [filter, setFilter] = useState('')
  const [creating, setCreating] = useState(false)
  // Bumped after a save so the chain redraws - a prerequisite edit changes the graph, not the
  // quest list, so nothing else would tell the panel to refetch.
  const [revision, setRevision] = useState(0)

  const selectedKey = questKey ?? null
  const selected = quests.find((q) => q.key === selectedKey) ?? null

  const visible = quests.filter((quest) => {
    if (!filter) return true
    const needle = filter.toLowerCase()
    return (
      quest.key.includes(needle) ||
      quest.name.toLowerCase().includes(needle) ||
      quest.zoneKey.includes(needle)
    )
  })

  return (
    <BuilderColumns
      left={
        <aside className="builder-col">
                <div className="tree">
                  <div className="tree-section">
                    <div className="tree-head">
                      <h3>Quests</h3>
                      <Button variant="link" onClick={() => setCreating(true)}>
                        + new
                      </Button>
                    </div>
        
                    <input
                      className="tree-filter"
                      value={filter}
                      placeholder="filter"
                      spellCheck={false}
                      onChange={(e) => setFilter(e.target.value)}
                    />
        
                    {quests.length === 0 && <p className="dim">None yet. Make one.</p>}
        
                    <ul className="template-list">
                      {visible.map((quest) => (
                        <li key={quest.key}>
                          {/* Plain button inside .tree, like the mob and item lists - `.tree li button`
                              already styles and highlights it. */}
                          <button
                            type="button"
                            className={quest.key === selectedKey ? 'selected' : ''}
                            onClick={() => navigate(toQuestsPath(quest.key))}
                          >
                            {quest.name || quest.key}
                            <span className="dim"> · {quest.zoneKey}</span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              </aside>
      }
      main={
        <main className="builder-col">
                {selectedKey ? (
                  <QuestEditor
                    key={selectedKey}
                    questKey={selectedKey}
                    onChanged={() => {
                      void refreshQuests()
                      setRevision((n) => n + 1)
                    }}
                    onDeleted={() => {
                      void refreshQuests()
                      setRevision((n) => n + 1)
                      navigate(toQuestsPath())
                    }}
                  />
                ) : (
                  <p className="dim">Select a quest, or create a new one.</p>
                )}
              </main>
      }
      right={
        <aside className="builder-col">
                <h3>Chain</h3>
                <StorylinePanel
                  zoneKey={selected?.zoneKey ?? null}
                  selectedKey={selectedKey}
                  onSelect={(key) => navigate(toQuestsPath(key))}
                  revision={revision}
                />
              </aside>
      }
    >
      <QuestCreateDialog
              open={creating}
              onOpenChange={setCreating}
              onCreated={(key) => {
                void refreshQuests()
                navigate(toQuestsPath(key))
              }}
            />
    </BuilderColumns>
  )
}
