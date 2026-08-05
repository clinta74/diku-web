import { useEffect, useState } from 'react'
import { api, type Account, type Character } from '../net/api'

const PATHS = ['Warden', 'Adept', 'Shade', 'Channeler'] as const

const PATH_BLURBS: Record<string, string> = {
  Warden: 'Armored frontline. High health, steady damage.',
  Adept: 'Focus-caster. Fragile, but hits from range.',
  Shade: 'Stealth and burst. Fast, and hard to pin down.',
  Channeler: 'Support and control. Shapes a fight rather than winning it alone.',
}

export function AuthScreen({ onReady }: { onReady: (account: Account) => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      const account =
        mode === 'login'
          ? await api.login(username, password)
          : await api.register(email, username, password)
      onReady(account)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="shell">
      <h1>diku-web</h1>
      <p className="tagline">Aldenmoor is open.</p>

      <form className="panel form" onSubmit={submit}>
        <div className="tabs">
          <button
            type="button"
            className={mode === 'login' ? 'active' : ''}
            onClick={() => setMode('login')}
          >
            Log in
          </button>
          <button
            type="button"
            className={mode === 'register' ? 'active' : ''}
            onClick={() => setMode('register')}
          >
            Register
          </button>
        </div>

        <label>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" />
        </label>

        {mode === 'register' && (
          <label>
            Email
            <input value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
          </label>
        )}

        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
          />
        </label>

        {error && <p className="bad">{error}</p>}

        <button type="submit" className="primary" disabled={busy}>
          {busy ? 'Working…' : mode === 'login' ? 'Log in' : 'Create account'}
        </button>
      </form>
    </main>
  )
}

export function CharacterScreen({
  onEnter,
  onLogout,
}: {
  onEnter: (character: Character) => void
  onLogout: () => void
}) {
  const [characters, setCharacters] = useState<Character[] | null>(null)
  const [name, setName] = useState('')
  const [path, setPath] = useState<string>(PATHS[0])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void api
      .characters()
      .then(setCharacters)
      .catch(() => setCharacters([]))
  }, [])

  async function create(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      const created = await api.createCharacter(name, path)
      setCharacters((current) => [...(current ?? []), created])
      setName('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create that character.')
    } finally {
      setBusy(false)
    }
  }

  async function enter(character: Character) {
    setError(null)
    try {
      await api.enter(character.id)
      onEnter(character)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not enter the world.')
    }
  }

  return (
    <main className="shell">
      <h1>diku-web</h1>
      <p className="tagline">Choose a character.</p>

      <section className="panel">
        <h2>Your characters</h2>
        {characters === null && <p className="dim">Loading…</p>}
        {characters?.length === 0 && <p className="dim">None yet. Make one below.</p>}

        <ul className="character-list">
          {characters?.map((character) => (
            <li key={character.id}>
              <button type="button" onClick={() => void enter(character)}>
                <strong>{character.name}</strong>
                <span className="dim">
                  {character.path} · level {character.level}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </section>

      <form className="panel form" onSubmit={create}>
        <h2>New character</h2>

        <label>
          Name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="3-16 letters"
            spellCheck={false}
          />
        </label>

        <fieldset className="paths">
          <legend>Path</legend>
          {PATHS.map((option) => (
            <label key={option} className={path === option ? 'path selected' : 'path'}>
              <input
                type="radio"
                name="path"
                value={option}
                checked={path === option}
                onChange={() => setPath(option)}
              />
              <strong>{option}</strong>
              <span className="dim">{PATH_BLURBS[option]}</span>
            </label>
          ))}
        </fieldset>

        {error && <p className="bad">{error}</p>}

        <button type="submit" className="primary" disabled={busy}>
          {busy ? 'Working…' : 'Create'}
        </button>
      </form>

      <button type="button" className="link" onClick={onLogout}>
        Log out
      </button>
    </main>
  )
}
