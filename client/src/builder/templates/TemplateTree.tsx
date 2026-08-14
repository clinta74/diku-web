import { useState } from 'react'
import { Button } from '../../ui/Button'
import { EntityFormDialog } from '../dialogs/EntityFormDialog'

interface TemplateTreeProps<T> {
  heading: string
  items: T[]
  keyOf: (item: T) => string
  labelOf: (item: T) => string
  badgeOf: (item: T) => string
  selectedKey: string | null
  onSelect: (key: string) => void
  createTitle: string
  createPlaceholder: string
  onCreate: (key: string, name: string) => Promise<void>
}

/**
 * The mob and item template lists were near-identical copies. This is the one component both
 * use, differing only in the label/badge accessors and the create body they pass in.
 */
export function TemplateTree<T>({
  heading,
  items,
  keyOf,
  labelOf,
  badgeOf,
  selectedKey,
  onSelect,
  createTitle,
  createPlaceholder,
  onCreate,
}: TemplateTreeProps<T>) {
  const [filter, setFilter] = useState('')
  const [creating, setCreating] = useState(false)

  const visible = items.filter((item) => {
    if (!filter) return true
    const needle = filter.toLowerCase()
    return keyOf(item).includes(needle) || labelOf(item).toLowerCase().includes(needle)
  })

  return (
    <div className="tree">
      <div className="tree-section">
        <div className="tree-head">
          <h3>{heading}</h3>
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

        <ul className="template-list">
          {visible.map((item) => {
            const key = keyOf(item)
            return (
              <li key={key}>
                <button
                  type="button"
                  className={key === selectedKey ? 'selected' : ''}
                  onClick={() => onSelect(key)}
                >
                  {labelOf(item) || key}
                  <span className="dim"> · {badgeOf(item)}</span>
                </button>
              </li>
            )
          })}
        </ul>
      </div>

      <EntityFormDialog
        open={creating}
        onOpenChange={setCreating}
        title={createTitle}
        keyLabel="Template key"
        keyPlaceholder={createPlaceholder}
        onSubmit={async ({ key, name }) => {
          await onCreate(key, name)
        }}
      />
    </div>
  )
}
