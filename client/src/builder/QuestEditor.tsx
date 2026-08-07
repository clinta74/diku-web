import { useEffect, useState } from 'react'
import { builderApi, type Quest, type MobTemplate } from '../net/builderApi'

interface Props {
  zoneKey: string
  onChanged: () => void
}

export function QuestEditor({ zoneKey, onChanged }: Props) {
  const [quests, setQuests] = useState<Quest[]>([])
  const [mobTemplates, setMobTemplates] = useState<MobTemplate[]>([])
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [editingKey, setEditingKey] = useState<string | null>(null)

  // Form state
  const [formKey, setFormKey] = useState('')
  const [name, setName] = useState('')
  const [summary, setSummary] = useState('')
  const [description, setDescription] = useState('')
  const [giverMobKey, setGiverMobKey] = useState('')
  const [turninMobKey, setTurninMobKey] = useState('')
  const [requiredItemKey, setRequiredItemKey] = useState('')
  const [requiredCount, setRequiredCount] = useState(1)
  const [rewardXp, setRewardXp] = useState(0)
  const [rewardGold, setRewardGold] = useState(0)
  const [rewardItemKey, setRewardItemKey] = useState('')
  const [rewardItemCount, setRewardItemCount] = useState(1)
  const [isRepeatable, setIsRepeatable] = useState(false)
  const [sortOrder, setSortOrder] = useState(0)

  // Load quests for this zone
  useEffect(() => {
    let cancelled = false

    void builderApi
      .quests()
      .then((loaded) => {
        if (cancelled) return
        const filtered = loaded.filter((q) => q.zoneKey === zoneKey)
        setQuests(filtered)
      })
      .catch(() => setQuests([]))

    return () => {
      cancelled = true
    }
  }, [zoneKey])

  // Load mob templates once
  useEffect(() => {
    void builderApi.mobTemplates().then(setMobTemplates).catch(() => setMobTemplates([]))
  }, [])

  function startEdit(quest: Quest) {
    setEditingKey(quest.key)
    setFormKey(quest.key)
    setName(quest.name)
    setSummary(quest.summary)
    setDescription(quest.description)
    setGiverMobKey(quest.giverMobKey)
    setTurninMobKey(quest.turninMobKey)
    setRequiredItemKey(quest.requiredItemKey || '')
    setRequiredCount(quest.requiredCount)
    setRewardXp(quest.rewardXp)
    setRewardGold(quest.rewardGold)
    setRewardItemKey(quest.rewardItemKey || '')
    setRewardItemCount(quest.rewardItemCount)
    setIsRepeatable(quest.isRepeatable)
    setSortOrder(quest.sortOrder)
    setError(null)
  }

  function startCreate() {
    setEditingKey('')
    setFormKey('')
    setName('')
    setSummary('')
    setDescription('')
    setGiverMobKey('')
    setTurninMobKey('')
    setRequiredItemKey('')
    setRequiredCount(1)
    setRewardXp(0)
    setRewardGold(0)
    setRewardItemKey('')
    setRewardItemCount(1)
    setIsRepeatable(false)
    setSortOrder(0)
    setError(null)
  }

  function cancelEdit() {
    setEditingKey(null)
    startCreate()
  }

  async function saveQuest() {
    if (!formKey) {
      setError('Quest key is required')
      return
    }

    if (!name) {
      setError('Quest name is required')
      return
    }

    if (!giverMobKey) {
      setError('Giver mob key is required')
      return
    }

    if (!turninMobKey) {
      setError('Turnin mob key is required')
      return
    }

    setBusy(true)
    setError(null)
    setSuccess(null)

    try {
      const body: Partial<Quest> = {
        zoneKey,
        name,
        summary,
        description,
        giverMobKey,
        turninMobKey,
        requiredItemKey: requiredItemKey || null,
        requiredCount,
        rewardXp,
        rewardGold,
        rewardItemKey: rewardItemKey || null,
        rewardItemCount,
        isRepeatable,
        sortOrder,
      }

      if (editingKey) {
        const updated = await builderApi.updateQuest(editingKey, body)
        setQuests(quests.map((q) => (q.key === editingKey ? updated : q)))
      } else {
        const created = await builderApi.createQuest(formKey, body)
        setQuests([...quests, created])
      }

      cancelEdit()
      setSuccess('Saved!')
      setTimeout(() => setSuccess(null), 2000)
      onChanged()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save quest')
    } finally {
      setBusy(false)
    }
  }

  async function deleteQuest(key: string) {
    if (!confirm(`Delete quest "${key}"?`)) {
      return
    }

    setBusy(true)
    setError(null)

    try {
      await builderApi.deleteQuest(key)
      setQuests(quests.filter((q) => q.key !== key))
      cancelEdit()
      setSuccess('Deleted!')
      setTimeout(() => setSuccess(null), 2000)
      onChanged()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not delete quest')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="quest-editor">
      <h2>Quests</h2>

      {error && <div className="error">{error}</div>}
      {success && <div className="success">{success}</div>}

      <div className="quest-list">
        <button onClick={startCreate} disabled={busy}>
          + New Quest
        </button>
        {quests.length === 0 ? (
          <p>No quests in this zone.</p>
        ) : (
          <ul>
            {quests.map((q) => (
              <li key={q.key} onClick={() => startEdit(q)} className={editingKey === q.key ? 'active' : ''}>
                <span className="quest-key">{q.key}</span>
                <span className="quest-name">{q.name}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      {editingKey !== null && (
        <div className="quest-form">
          <h3>{editingKey ? 'Edit Quest' : 'New Quest'}</h3>

          <div className="form-group">
            <label>
              Quest Key:
              <input
                type="text"
                value={formKey}
                onChange={(e) => setFormKey(e.target.value)}
                disabled={!!editingKey}
                placeholder="aldenmoor.quest-name"
              />
            </label>
          </div>

          <div className="form-group">
            <label>
              Name:
              <input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="Quest Name" />
            </label>
          </div>

          <div className="form-group">
            <label>
              Summary:
              <textarea
                value={summary}
                onChange={(e) => setSummary(e.target.value)}
                placeholder="One-line summary"
                rows={2}
              />
            </label>
          </div>

          <div className="form-group">
            <label>
              Description:
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Full description"
                rows={4}
              />
            </label>
          </div>

          <div className="form-row">
            <div className="form-group">
              <label>
                Giver Mob:
                <select value={giverMobKey} onChange={(e) => setGiverMobKey(e.target.value)}>
                  <option value="">-- Select Mob --</option>
                  {mobTemplates.map((m) => (
                    <option key={m.key} value={m.key}>
                      {m.key}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            <div className="form-group">
              <label>
                Turnin Mob:
                <select value={turninMobKey} onChange={(e) => setTurninMobKey(e.target.value)}>
                  <option value="">-- Select Mob --</option>
                  {mobTemplates.map((m) => (
                    <option key={m.key} value={m.key}>
                      {m.key}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </div>

          <div className="form-row">
            <div className="form-group">
              <label>
                Required Item:
                <input
                  type="text"
                  value={requiredItemKey}
                  onChange={(e) => setRequiredItemKey(e.target.value)}
                  placeholder="(optional)"
                />
              </label>
            </div>

            <div className="form-group">
              <label>
                Required Count:
                <input type="number" value={requiredCount} onChange={(e) => setRequiredCount(parseInt(e.target.value))} />
              </label>
            </div>
          </div>

          <div className="form-row">
            <div className="form-group">
              <label>
                Reward XP:
                <input type="number" value={rewardXp} onChange={(e) => setRewardXp(parseInt(e.target.value))} />
              </label>
            </div>

            <div className="form-group">
              <label>
                Reward Gold:
                <input type="number" value={rewardGold} onChange={(e) => setRewardGold(parseInt(e.target.value))} />
              </label>
            </div>
          </div>

          <div className="form-row">
            <div className="form-group">
              <label>
                Reward Item:
                <input
                  type="text"
                  value={rewardItemKey}
                  onChange={(e) => setRewardItemKey(e.target.value)}
                  placeholder="(optional)"
                />
              </label>
            </div>

            <div className="form-group">
              <label>
                Reward Count:
                <input
                  type="number"
                  value={rewardItemCount}
                  onChange={(e) => setRewardItemCount(parseInt(e.target.value))}
                />
              </label>
            </div>
          </div>

          <div className="form-group">
            <label>
              <input
                type="checkbox"
                checked={isRepeatable}
                onChange={(e) => setIsRepeatable(e.target.checked)}
              />
              Repeatable
            </label>
          </div>

          <div className="form-group">
            <label>
              Sort Order:
              <input type="number" value={sortOrder} onChange={(e) => setSortOrder(parseInt(e.target.value))} />
            </label>
          </div>

          <div className="form-actions">
            <button onClick={saveQuest} disabled={busy}>
              {editingKey ? 'Update' : 'Create'}
            </button>
            {editingKey && (
              <button onClick={() => deleteQuest(editingKey)} disabled={busy} className="delete-btn">
                Delete
              </button>
            )}
            <button onClick={cancelEdit} disabled={busy}>
              Cancel
            </button>
          </div>
        </div>
      )}

      <style>{`
        .quest-editor {
          display: flex;
          gap: 20px;
          padding: 20px;
        }

        .quest-list {
          flex: 1;
        }

        .quest-list ul {
          list-style: none;
          padding: 0;
          margin: 10px 0;
        }

        .quest-list li {
          padding: 8px;
          border: 1px solid #ccc;
          margin-bottom: 5px;
          cursor: pointer;
          border-radius: 4px;
        }

        .quest-list li:hover {
          background-color: #f0f0f0;
        }

        .quest-list li.active {
          background-color: #e0e0e0;
          font-weight: bold;
        }

        .quest-key {
          font-family: monospace;
          font-size: 0.9em;
          color: #666;
          display: block;
        }

        .quest-name {
          display: block;
          font-weight: bold;
        }

        .quest-form {
          flex: 2;
          border: 1px solid #ccc;
          padding: 15px;
          border-radius: 4px;
          background-color: #fafafa;
        }

        .form-group {
          margin-bottom: 12px;
        }

        .form-group label {
          display: flex;
          flex-direction: column;
          gap: 4px;
          font-weight: bold;
        }

        .form-group input,
        .form-group select,
        .form-group textarea {
          padding: 6px;
          border: 1px solid #ccc;
          border-radius: 4px;
          font-family: inherit;
        }

        .form-row {
          display: grid;
          grid-template-columns: 1fr 1fr;
          gap: 15px;
        }

        .form-actions {
          display: flex;
          gap: 10px;
          margin-top: 20px;
        }

        .form-actions button {
          padding: 8px 16px;
          border: 1px solid #ccc;
          border-radius: 4px;
          background-color: white;
          cursor: pointer;
        }

        .form-actions button:hover:not(:disabled) {
          background-color: #e0e0e0;
        }

        .form-actions button:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }

        .delete-btn {
          background-color: #fee;
          border-color: #c99;
        }

        .delete-btn:hover:not(:disabled) {
          background-color: #fdd;
        }

        .error {
          color: red;
          background-color: #ffe0e0;
          padding: 10px;
          border-radius: 4px;
          margin-bottom: 10px;
        }

        .success {
          color: green;
          background-color: #e0ffe0;
          padding: 10px;
          border-radius: 4px;
          margin-bottom: 10px;
        }
      `}</style>
    </div>
  )
}
