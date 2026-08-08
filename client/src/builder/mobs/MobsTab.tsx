import { useNavigate, useParams } from 'react-router'
import { builderApi } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { toMobsPath } from '../routes'
import { TemplateTree } from '../templates/TemplateTree'
import { MobTemplateEditor } from './MobTemplateEditor'

export function MobsTab() {
  const navigate = useNavigate()
  const { templateKey } = useParams()
  const { mobTemplates, refreshMobTemplates } = useBuilderData()
  const selectedKey = templateKey ?? null

  return (
    <div className="builder-columns builder-columns-2">
      <aside className="builder-col">
        <TemplateTree
          heading="Mob templates"
          items={mobTemplates}
          keyOf={(t) => t.key}
          labelOf={(t) => t.name}
          badgeOf={(t) => `L${t.level}`}
          selectedKey={selectedKey}
          onSelect={(key) => navigate(toMobsPath(key))}
          createTitle="New mob template"
          createPlaceholder="warden-mentor"
          onCreate={async (key, name) => {
            await builderApi.createMobTemplate(key, {
              name: name || key,
              description: '',
              icon: 'm',
              level: 1,
              baseStats: {},
              baseXp: 0,
              baseGold: 0,
              loot: [],
              behavior: {},
              attacks: [],
            })
            await refreshMobTemplates()
            navigate(toMobsPath(key))
          }}
        />
      </aside>

      <main className="builder-col">
        {selectedKey ? (
          <MobTemplateEditor
            key={selectedKey}
            templateKey={selectedKey}
            onChanged={() => void refreshMobTemplates()}
            onDeleted={() => {
              void refreshMobTemplates()
              navigate(toMobsPath())
            }}
          />
        ) : (
          <p className="dim">Select a mob template, or create a new one.</p>
        )}
      </main>
    </div>
  )
}
