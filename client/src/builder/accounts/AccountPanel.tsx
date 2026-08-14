import { useState } from 'react'
import {
  adminApi,
  isMuted,
  MUTE_DURATIONS,
  ROLES,
  ROLE_BLURBS,
  type AdminAccount,
  type Role,
} from '../../net/adminApi'
import { Button } from '../../ui/Button'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import { useToast } from '../../ui/Toast'

/** Matches PasswordPolicy on the server; checked here to save a round trip. */
const MIN_PASSWORD_LENGTH = 8

interface AccountPanelProps {
  account: AdminAccount
  onChanged: (account: AdminAccount) => void
}

function when(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : 'never'
}

/**
 * One account, and everything an admin can do to it (PLAN.md §7.7, §8).
 *
 * The two destructive-in-different-ways actions — setting a password and retiring a character —
 * go through a confirmation, because neither has an undo an admin can reach: a password cannot be
 * put back, and a retired character can only be restored from the database by hand.
 */
export function AccountPanel({ account, onChanged }: AccountPanelProps) {
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const [role, setRole] = useState<Role>(account.role as Role)
  const [reason, setReason] = useState('')
  const [muteMinutes, setMuteMinutes] = useState(String(MUTE_DURATIONS[1].minutes))
  const [password, setPassword] = useState('')
  const [confirming, setConfirming] = useState<'password' | { character: string } | null>(null)

  const muted = isMuted(account)

  /**
   * One wrapper for every call: they all end in either a fresh account or a message to show, and
   * spelling that out at six call sites is how five of them end up without error handling.
   */
  async function run(action: () => Promise<AdminAccount>, success: string) {
    setBusy(true)
    try {
      onChanged(await action())
      toast.notify(success)
      return true
    } catch (e) {
      toast.notify(e instanceof Error ? e.message : 'That did not work.', 'bad')
      return false
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="template-editor">
      <div className="room-editor-head">
        <h2>{account.username}</h2>
        <span className="dim">{account.email}</span>
      </div>

      <section className="room-section">
        <dl className="account-facts">
          <div>
            <dt>Role</dt>
            <dd>{account.role}</dd>
          </div>
          <div>
            <dt>Created</dt>
            <dd>{when(account.createdAt)}</dd>
          </div>
          <div>
            <dt>Last login</dt>
            <dd>{when(account.lastLoginAt)}</dd>
          </div>
          <div>
            <dt>Status</dt>
            <dd>
              {account.isBanned ? (
                <span className="bad">Banned{account.banReason ? ` — ${account.banReason}` : ''}</span>
              ) : muted ? (
                <span className="dim">Muted until {when(account.mutedUntil)}</span>
              ) : (
                <span className="good">Active</span>
              )}
            </dd>
          </div>
        </dl>
      </section>

      <section className="room-section">
        <h3>Role</h3>
        <div className="section-body">
          <Field label="Role" hint={ROLE_BLURBS[role]}>
            <Select value={role} onChange={(value) => setRole(value as Role)} disabled={busy}>
              {ROLES.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </Select>
          </Field>

          <Button
           
            variant="primary"
            disabled={busy || role === account.role}
            onClick={() =>
              void run(
                () => adminApi.setRole(account.username, role),
                `${account.username} is now ${role}.`,
              )
            }
          >
            Change role
          </Button>
        </div>
      </section>

      <section className="room-section">
        <h3>Moderation</h3>
        <div className="section-body">
          <Field
            label="Reason"
            hint="Shown to them when they are banned, and kept in the audit trail either way."
          >
            <input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="optional"
            />
          </Field>

          <div className="account-actions">
            <button
              type="button"
              className={account.isBanned ? '' : 'danger-button'}
              disabled={busy}
              onClick={() =>
                void run(
                  () => adminApi.setBan(account.username, !account.isBanned, reason),
                  account.isBanned
                    ? `${account.username} may sign in again.`
                    : `${account.username} is banned.`,
                ).then((ok) => ok && setReason(''))
              }
            >
              {account.isBanned ? 'Lift ban' : 'Ban'}
            </button>

            {muted ? (
              <button
                type="button"
                disabled={busy}
                onClick={() =>
                  void run(
                    () => adminApi.setMute(account.username, null, reason),
                    `${account.username} may speak again.`,
                  )
                }
              >
                Unmute
              </button>
            ) : (
              <>
                <Select
                  value={muteMinutes}
                  onChange={setMuteMinutes}
                  disabled={busy}
                  aria-label="Mute duration"
                >
                  {MUTE_DURATIONS.map((option) => (
                    <option key={option.minutes} value={option.minutes}>
                      {option.label}
                    </option>
                  ))}
                </Select>

                <button
                  type="button"
                  disabled={busy}
                  onClick={() =>
                    void run(
                      () => adminApi.setMute(account.username, Number(muteMinutes), reason),
                      `${account.username} is muted.`,
                    ).then((ok) => ok && setReason(''))
                  }
                >
                  Mute
                </button>
              </>
            )}
          </div>
        </div>
      </section>

      <section className="room-section">
        <h3>Password</h3>
        <div className="section-body">
          <Field
            label="New password"
            hint="There is no password email in this deployment, so this is the only way back in for someone locked out. Tell them out of band, and expect them to change it."
          >
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="new-password"
              placeholder={`at least ${MIN_PASSWORD_LENGTH} characters`}
            />
          </Field>

          <Button
           
            variant="danger"
            disabled={busy || password.length < MIN_PASSWORD_LENGTH}
            onClick={() => setConfirming('password')}
          >
            Set password
          </Button>
        </div>
      </section>

      <section className="room-section">
        <h3>Characters</h3>
        <div className="section-body">
          {account.characters.length === 0 ? (
            <p className="dim">No characters.</p>
          ) : (
            <ul className="spawner-list">
              {account.characters.map((character) => (
                <li key={character}>
                  <span className="spawner-info">{character}</span>
                  <span className="spawner-actions">
                    <Button
                     
                      variant="link"
                      disabled={busy}
                      onClick={() => setConfirming({ character })}
                    >
                      Retire
                    </Button>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      <ConfirmDialog
        open={confirming === 'password'}
        onOpenChange={(open) => !open && setConfirming(null)}
        title={`Set ${account.username}'s password?`}
        description="They will be signed out everywhere and their characters will leave the world. The action is recorded against your account."
        confirmLabel="Set password"
        destructive
        busy={busy}
        onConfirm={() =>
          void run(
            () => adminApi.setPassword(account.username, password),
            `${account.username}'s password has been set.`,
          ).then((ok) => {
            if (ok) setPassword('')
            setConfirming(null)
          })
        }
      />

      <ConfirmDialog
        open={typeof confirming === 'object' && confirming !== null}
        onOpenChange={(open) => !open && setConfirming(null)}
        title={
          typeof confirming === 'object' && confirming !== null
            ? `Retire ${confirming.character}?`
            : 'Retire character?'
        }
        description="The character leaves the account and frees its name. Its row, items, and quest progress are kept, so this can be undone in the database — but not from here."
        confirmLabel="Retire"
        destructive
        busy={busy}
        onConfirm={() => {
          if (typeof confirming !== 'object' || confirming === null) return
          const { character } = confirming

          setBusy(true)
          void adminApi
            .deleteCharacter(character)
            // The delete answers with a message rather than the account, so this is the one
            // action that has to ask what the account looks like afterwards.
            .then(() => adminApi.account(account.username))
            .then((updated) => {
              onChanged(updated)
              toast.notify(`${character} has been retired.`)
            })
            .catch((e: unknown) =>
              toast.notify(e instanceof Error ? e.message : 'That did not work.', 'bad'),
            )
            .finally(() => {
              setBusy(false)
              setConfirming(null)
            })
        }}
      />
    </div>
  )
}
