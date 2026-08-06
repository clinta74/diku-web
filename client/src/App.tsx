import { useCallback, useEffect, useRef, useState } from 'react'
import { AuthScreen, CharacterScreen } from './components/AuthScreen'
import { GameScreen } from './components/GameScreen'
import { BuilderScreen } from './builder/BuilderScreen'
import { api, type Account, type Character } from './net/api'
import './App.css'

type Stage =
  | { name: 'loading' }
  | { name: 'anonymous' }
  | { name: 'choosing'; account: Account }
  | { name: 'playing'; account: Account; character: Character }

const BUILDER_ROLES = ['Builder', 'Admin']

export default function App() {
  const [stage, setStage] = useState<Stage>({ name: 'loading' })
  const [builderOpen, setBuilderOpen] = useState(false)
  const [currentRoom, setCurrentRoom] = useState<string | null>(null)
  const focusInputRef = useRef<(() => void) | null>(null)

  // The session cookie survives a reload, so check for an existing login before showing
  // the form - otherwise a refresh mid-session looks like being logged out.
  useEffect(() => {
    void api
      .me()
      .then((account) => setStage({ name: 'choosing', account }))
      .catch(() => setStage({ name: 'anonymous' }))
  }, [])

  // Stable so GameScreen's effect does not re-fire on every render of App.
  const onRoomChange = useCallback((roomKey: string) => setCurrentRoom(roomKey), [])

  // Focus input when returning from builder
  useEffect(() => {
    if (!builderOpen) {
      focusInputRef.current?.()
    }
  }, [builderOpen])

  async function logout() {
    await api.logout().catch(() => undefined)
    setBuilderOpen(false)
    setStage({ name: 'anonymous' })
  }

  switch (stage.name) {
    case 'loading':
      return (
        <main className="shell">
          <p className="dim">Connecting…</p>
        </main>
      )

    case 'anonymous':
      return <AuthScreen onReady={(account) => setStage({ name: 'choosing', account })} />

    case 'choosing':
      return (
        <CharacterScreen
          onEnter={(character) => setStage({ name: 'playing', account: stage.account, character })}
          onLogout={() => void logout()}
        />
      )

    case 'playing': {
      const canBuild = BUILDER_ROLES.includes(stage.account.role)

      return (
        <>
          {/* Hidden rather than unmounted while the builder is open. Unmounting would close
              the SSE stream, mark the character link-dead, and reconnect on the way back -
              and follow mode depends on that stream staying live to know where you are. */}
          <div className={builderOpen ? 'workspace hidden' : 'workspace'}>
            <GameScreen
              characterId={stage.character.id}
              characterName={stage.character.name}
              onRoomChange={onRoomChange}
              onOpenBuilder={canBuild ? () => setBuilderOpen(true) : undefined}
              onLeave={() => {
                // Frees the slot against the per-account cap straight away rather than waiting
                // out the 90 s link-dead window.
                void api.leave(stage.character.id).catch(() => undefined)
                setBuilderOpen(false)
                setStage({ name: 'choosing', account: stage.account })
              }}
              focusInputRef={focusInputRef}
            />
          </div>

          {builderOpen && canBuild && (
            <BuilderScreen occupiedRoom={currentRoom} onClose={() => setBuilderOpen(false)} />
          )}
        </>
      )
    }
  }
}
