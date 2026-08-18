import { useNavigate, useParams } from 'react-router'
import { BuilderColumns } from '../BuilderColumns'
import { builderApi } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { toItemsPath } from '../routes'
import { TemplateTree } from '../templates/TemplateTree'
import { ItemTemplateEditor } from './ItemTemplateEditor'

export function ItemsTab() {
  const navigate = useNavigate()
  const { templateKey } = useParams()
  const { itemTemplates, refreshItemTemplates } = useBuilderData()
  const selectedKey = templateKey ?? null

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
                    onChanged={() => void refreshItemTemplates()}
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
    />
  )
}
