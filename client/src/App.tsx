import { useEffect, useState } from 'react'
import { AuthScreen, CharacterScreen } from './components/AuthScreen'
import { GameScreen } from './components/GameScreen'
import { api, type Account, type Character } from './net/api'
import './App.css'

type Stage =
  | { name: 'loading' }
  | { name: 'anonymous' }
  | { name: 'choosing'; account: Account }
  | { name: 'playing'; account: Account; character: Character }

export default function App() {
  const [stage, setStage] = useState<Stage>({ name: 'loading' })

  // The session cookie survives a reload, so check for an existing login before showing
  // the form - otherwise a refresh mid-session looks like being logged out.
  useEffect(() => {
    void api
      .me()
      .then((account) => setStage({ name: 'choosing', account }))
      .catch(() => setStage({ name: 'anonymous' }))
  }, [])

  async function logout() {
    await api.logout().catch(() => undefined)
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

    case 'playing':
      return (
        <GameScreen
          characterName={stage.character.name}
          onLeave={() => setStage({ name: 'choosing', account: stage.account })}
        />
      )
  }
}
