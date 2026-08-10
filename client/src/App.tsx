import { useCallback, useEffect, useRef, useState } from 'react'
import { Navigate, Route, Routes, useLocation, useNavigate } from 'react-router'
import { AuthScreen, CharacterScreen } from './components/AuthScreen'
import { GameScreen } from './components/GameScreen'
import { BuilderShell } from './builder/BuilderShell'
import { WorldTab } from './builder/world/WorldTab'
import { MobsTab } from './builder/mobs/MobsTab'
import { ItemsTab } from './builder/items/ItemsTab'
import { QuestsTab } from './builder/quests/QuestsTab'
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
  const [currentRoom, setCurrentRoom] = useState<string | null>(null)
  const [follow, setFollow] = useState(true)
  const focusInputRef = useRef<(() => void) | null>(null)
  const navigate = useNavigate()
  const location = useLocation()
  const inBuilder = location.pathname.startsWith('/builder')

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

  // Focus the game input when returning from the builder.
  useEffect(() => {
    if (!inBuilder) {
      focusInputRef.current?.()
    }
  }, [inBuilder])

  async function logout() {
    await api.logout().catch(() => undefined)
    navigate('/')
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
              and follow mode depends on that stream staying live to know where you are.
              GameScreen sits OUTSIDE <Routes> for the same reason: route changes must not
              remount it. */}
          <div className={inBuilder ? 'workspace hidden' : 'workspace'}>
            <GameScreen
              characterId={stage.character.id}
              characterName={stage.character.name}
              onRoomChange={onRoomChange}
              // A path opens the builder on that exact entity — the deep links `examine` and
              // `stats` hand a builder. No path is the plain "open the builder" button.
              //
              // The typeof guard is deliberate: an optional-parameter callback is assignable to
              // `() => void`, so a caller that wires this straight to onClick type-checks and
              // then passes a MouseEvent as the "path". That is exactly how the builder button
              // broke, and it broke silently.
              onOpenBuilder={
                canBuild
                  ? (path?: string) => navigate(typeof path === 'string' ? path : '/builder')
                  : undefined
              }
              onLeave={() => {
                // Frees the slot against the per-account cap straight away rather than waiting
                // out the 90 s link-dead window.
                void api.leave(stage.character.id).catch(() => undefined)
                navigate('/')
                setStage({ name: 'choosing', account: stage.account })
              }}
              focusInputRef={focusInputRef}
              // Hidden is not unmounted, so the game has to be told to stop listening for
              // keystrokes - otherwise typing a room name in the builder would type it here.
              active={!inBuilder}
            />
          </div>

          <Routes>
            <Route
              path="/builder"
              element={
                canBuild ? (
                  <BuilderShell
                    occupiedRoom={currentRoom}
                    follow={follow}
                    onFollowChange={setFollow}
                    onClose={() => navigate('/')}
                  />
                ) : (
                  <Navigate to="/" replace />
                )
              }
            >
              <Route index element={<Navigate to="world" replace />} />
              <Route path="world/:world?/:zone?/:room?/:section?" element={<WorldTab />} />
              <Route path="mobs/:templateKey?" element={<MobsTab />} />
              <Route path="items/:templateKey?" element={<ItemsTab />} />
              <Route path="quests/:questKey?" element={<QuestsTab />} />
            </Route>
          </Routes>
        </>
      )
    }
  }
}
