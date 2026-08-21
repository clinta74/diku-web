import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { BuilderColumns } from '../BuilderColumns'
import { builderApi } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { toItemsPath } from '../routes'
import { TemplatePlacementPanel } from '../templates/TemplatePlacementPanel'
import { TemplateTree } from '../templates/TemplateTree'
import { ItemTemplateEditor } from './ItemTemplateEditor'

/**
 * The Items tab.
 *
 * The placement rail (§7.9) matters more here than on Mobs: an item's own spawner is the rarest
 * of its four sources, so "where does this come from" is a question the item's record cannot even
 * partially answer — it is spread across loot tables, shop stock, and quest rewards, none of which
 * the item knows about.
 */
export function ItemsTab() {
  const navigate = useNavigate()
  const { templateKey } = useParams()
  const { itemTemplates, refreshItemTemplates } = useBuilderData()
  const selectedKey = templateKey ?? null

  const [revision, setRevision] = useState(0)

  return (
    <BuilderColumns
      left={
        <aside className="builder-col">
                <TemplateTree
                  heading="Item templates"
                  items={itemTemplates}
                  keyOf={(t) => t.key}
                  labelOf={(t) => t.name}
                  badgeOf={(t) => (t.isTwoHanded ? 'both hands' : (t.slots.join('/') || 'ground'))}
                  selectedKey={selectedKey}
                  onSelect={(key) => navigate(toItemsPath(key))}
                  createTitle="New item template"
                  createPlaceholder="rusted-blade"
                  onCreate={async (key, name) => {
                    await builderApi.createItemTemplate(key, {
                      name: name || key,
                      description: '',
                      icon: 'i',
                      slots: [],
                      isTwoHanded: false,
                      weight: 0,
                      baseValue: 0,
                      baseStats: {},
                      attackDelayPulses: null,
                      attackVerb: null,
                    })
                    await refreshItemTemplates()
                    navigate(toItemsPath(key))
                  }}
                />
              </aside>
      }
      main={
        <main className="builder-col">
                {selectedKey ? (
                  <ItemTemplateEditor
                    key={selectedKey}
                    templateKey={selectedKey}
                    onChanged={() => {
                      void refreshItemTemplates()
                      setRevision((n) => n + 1)
                    }}
                    onDeleted={() => {
                      void refreshItemTemplates()
                      navigate(toItemsPath())
                    }}
                  />
                ) : (
                  <p className="dim">Select an item template, or create a new one.</p>
                )}
              </main>
      }
      right={
        <aside className="builder-col">
                <h3>Where it comes from</h3>
                <TemplatePlacementPanel kind="item" templateKey={selectedKey} revision={revision} />
              </aside>
      }
    />
  )
}
