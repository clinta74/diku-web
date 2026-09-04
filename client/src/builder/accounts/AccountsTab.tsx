import { useCallback, useEffect, useState } from 'react'
import { BuilderColumns } from '../BuilderColumns'
import { useNavigate, useParams } from 'react-router'
import { adminApi, isMuted, type AdminAccount } from '../../net/adminApi'
import { toAccountsPath } from '../routes'
import { AccountPanel } from './AccountPanel'

/**
 * Account administration (PLAN.md §7.7, §8) — the Admin-only builder tab.
 *
 * Unlike the world tabs this holds no shared cache: accounts change from outside the panel (a
 * login, an in-game `ban`, someone registering), so a list kept warm across visits would be a
 * list that is quietly wrong. Every action returns the account as the server now sees it, and
 * that answer replaces the row rather than the panel re-fetching and hoping.
 */
export function AccountsTab() {
  const navigate = useNavigate()
  const { username } = useParams()
  const [query, setQuery] = useState('')
  const [accounts, setAccounts] = useState<AdminAccount[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const selected = accounts?.find((a) => a.username === username) ?? null

  // Debounced, because this searches the account table on every keystroke otherwise.
  useEffect(() => {
    const handle = setTimeout(() => {
      void adminApi
        .accounts(query.trim() || undefined)
        .then((rows) => {
          setAccounts(rows)
          setError(null)
        })
        .catch((e: unknown) => {
          setAccounts([])
          setError(e instanceof Error ? e.message : 'Could not load accounts.')
        })
    }, 250)

    return () => clearTimeout(handle)
  }, [query])

  /** Folds an action's answer back into the list, so nothing has to be re-fetched. */
  const replace = useCallback((account: AdminAccount) => {
    setAccounts((current) =>
      current?.map((a) => (a.id === account.id ? account : a)) ?? [account],
    )
  }, [])

  // A URL naming somebody outside the current search still has to resolve - a link from
  // elsewhere, or a filter typed after the fact. Fetch that one account on its own.
  useEffect(() => {
    if (!username || selected || accounts === null) return

    void adminApi
      .account(username)
      .then((account) => setAccounts((current) => [...(current ?? []), account]))
      .catch(() => undefined)
  }, [username, selected, accounts])

  return (
    <BuilderColumns
      left={
        <aside className="builder-col">
                <div className="tree">
                  <div className="tree-section">
                    <div className="tree-head">
                      <h3>Accounts</h3>
                      <span className="dim">{accounts?.length ?? 0}</span>
                    </div>
        
                    <input
                      className="tree-filter"
                      value={query}
                      placeholder="search by username or address"
                      spellCheck={false}
                      onChange={(e) => setQuery(e.target.value)}
                    />
        
                    {error && <p className="bad">{error}</p>}
                    {accounts === null && <p className="dim">Loading…</p>}
                    {accounts?.length === 0 && !error && <p className="dim">Nobody matches that.</p>}
        
                    <ul className="template-list">
                      {accounts?.map((account) => (
                        <li key={account.id}>
                          <button
                            type="button"
                            className={account.username === username ? 'selected' : ''}
                            onClick={() => navigate(toAccountsPath(account.username))}
                          >
                            {account.username}
                            <span className="dim"> · {account.role}</span>
                            {account.isBanned && <span className="bad"> · banned</span>}
                            {!account.isBanned && isMuted(account) && (
                              <span className="dim"> · muted</span>
                            )}
                          </button>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              </aside>
      }
      main={
        <main className="builder-col">
                {selected ? (
                  <AccountPanel key={selected.id} account={selected} onChanged={replace} />
                ) : (
                  <p className="dim">Select an account.</p>
                )}
              </main>
      }
    />
  )
}
