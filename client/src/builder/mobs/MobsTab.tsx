import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { BuilderColumns } from '../BuilderColumns'
import { builderApi } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { toMobsPath } from '../routes'
import { TemplatePlacementPanel } from '../templates/TemplatePlacementPanel'
import { TemplateTree } from '../templates/TemplateTree'
import { MobTemplateEditor } from './MobTemplateEditor'

/**
 * The Mobs tab.
 *
 * Three columns since the placement rail landed (§7.9). A template is authored in isolation but
 * never *exists* in isolation, and where it stands is stored on the spawner rather than on the
 * template — so it is the one thing about a mob that its own editor structurally cannot show.
 */
export function MobsTab() {
  const navigate = useNavigate()
  const { templateKey } = useParams()
  const { mobTemplates, refreshMobTemplates } = useBuilderData()
  const selectedKey = templateKey ?? null

  // Bumped after a save so the rail redraws - a rename changes what the spawner rows say about
  // the zone they sit in, and nothing else would tell the panel to refetch.
  const [revision, setRevision] = useState(0)

  return (
    <BuilderColumns
      left={
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
      }
      main={
        <main className="builder-col">
                {selectedKey ? (
                  <MobTemplateEditor
                    key={selectedKey}
                    templateKey={selectedKey}
                    onChanged={() => {
                      void refreshMobTemplates()
                      setRevision((n) => n + 1)
                    }}
                    onDeleted={() => {
                      void refreshMobTemplates()
                      navigate(toMobsPath())
                    }}
                  />
                ) : (
                  <p className="dim">Select a mob template, or create a new one.</p>
                )}
              </main>
      }
      right={
        <aside className="builder-col">
                <h3>Where it lives</h3>
                <TemplatePlacementPanel kind="mob" templateKey={selectedKey} revision={revision} />
              </aside>
      }
    />
  )
}
