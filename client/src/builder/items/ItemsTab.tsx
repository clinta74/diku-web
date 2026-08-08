import { useNavigate, useParams } from 'react-router'
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
    <div className="builder-columns builder-columns-2">
      <aside className="builder-col">
        <TemplateTree
          heading="Item templates"
          items={itemTemplates}
          keyOf={(t) => t.key}
          labelOf={(t) => t.name}
          badgeOf={(t) => t.slot ?? 'ground'}
          selectedKey={selectedKey}
          onSelect={(key) => navigate(toItemsPath(key))}
          createTitle="New item template"
          createPlaceholder="rusted-blade"
          onCreate={async (key, name) => {
            await builderApi.createItemTemplate(key, {
              name: name || key,
              description: '',
              icon: 'i',
              slot: null,
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
    </div>
  )
}
