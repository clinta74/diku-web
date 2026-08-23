// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { DraftField } from './DraftPanel'
import { DraftPanel } from './DraftPanel'

afterEach(cleanup)

const fields: DraftField[] = [
  { key: 'title', label: 'Title', value: 'The Tollhouse Steps' },
  { key: 'description', label: 'Description', value: 'Stone steps, worn smooth and slick with moss.' },
]

function renderPanel(props: Partial<Parameters<typeof DraftPanel>[0]> = {}) {
  const handlers = { onUse: vi.fn(), onDiscard: vi.fn() }

  render(
    <DraftPanel
      status="ready"
      elapsed={0}
      fields={fields}
      warnings={[]}
      error={null}
      {...handlers}
      {...props}
    />,
  )

  return handlers
}

describe('while it is working', () => {
  /**
   * The whole reason the panel exists in the working state. A three-minute wait with nothing on
   * screen is indistinguishable from a broken button, and that is the state a builder is in for
   * most of the time they spend with this feature.
   */
  it('says it is working, and how long it has been', () => {
    renderPanel({ status: 'working', elapsed: 7, fields: [] })

    expect(screen.getByRole('status').textContent).toContain('Drafting… 7s')
  })

  it('counts past a minute in minutes', () => {
    renderPanel({ status: 'working', elapsed: 95, fields: [] })

    expect(screen.getByRole('status').textContent).toContain('1m 35s')
  })

  /**
   * The explanation arrives late on purpose. Said immediately it is noise on a fast draft; said
   * never, a builder twenty seconds in concludes it has hung.
   */
  it('explains the wait only once the wait is odd', () => {
    renderPanel({ status: 'working', elapsed: 5, fields: [] })
    expect(screen.queryByText(/takes a few minutes/)).toBeNull()

    cleanup()

    renderPanel({ status: 'working', elapsed: 25, fields: [] })
    expect(screen.getByText(/takes a few minutes/)).toBeTruthy()
  })

  it('offers nothing to accept while there is nothing to accept', () => {
    renderPanel({ status: 'working', elapsed: 5, fields: [] })

    expect(screen.queryByRole('button', { name: 'Use all' })).toBeNull()
  })
})

describe('when it has something', () => {
  it('shows the draft without applying it', () => {
    const handlers = renderPanel()

    expect(screen.getByText('The Tollhouse Steps')).toBeTruthy()
    expect(screen.getByText(/worn smooth/)).toBeTruthy()

    // Nothing has been called: showing a draft is not taking it.
    expect(handlers.onUse).not.toHaveBeenCalled()
  })

  /**
   * Title and description are taken separately because they fail separately - the prose is often
   * worth keeping when the title is not. Forcing both is what turns a useful suggestion into one
   * people stop pressing.
   */
  it.each([
    ['Use all', ['title', 'description']],
    ['Title only', ['title']],
    ['Description only', ['description']],
  ] as const)('%s applies %s', (label, keys) => {
    const handlers = renderPanel()

    fireEvent.click(screen.getByRole('button', { name: label }))

    expect(handlers.onUse).toHaveBeenCalledWith([...keys])
  })

  it('discards without applying anything', () => {
    const handlers = renderPanel()

    fireEvent.click(screen.getByRole('button', { name: 'Discard' }))

    expect(handlers.onDiscard).toHaveBeenCalledOnce()
    expect(handlers.onUse).not.toHaveBeenCalled()
  })

  /**
   * The panel knows nothing about rooms, which is what lets the mob, item and quest editors reuse
   * it rather than growing a second copy that drifts.
   */
  it('offers whatever fields it is given', () => {
    renderPanel({
      fields: [
        { key: 'name', label: 'Name', value: 'a rim-wolf' },
        { key: 'summary', label: 'Summary', value: 'Cull the pack' },
        { key: 'description', label: 'Description', value: 'Lean, and watching.' },
      ],
    })

    expect(screen.getByRole('button', { name: 'Summary only' })).toBeTruthy()
    expect(screen.getByText('a rim-wolf')).toBeTruthy()
  })

  /**
   * Warnings are shown beside the draft, before it is taken.
   *
   * These are the things the output grammar could not prevent - two exits the same way, prose
   * describing a door - and they are easy to miss precisely because the draft reads well. After
   * the save would be too late to be a review.
   */
  it('shows the warnings with the draft', () => {
    renderPanel({ warnings: ['names north 2 times; a room has one exit per direction'] })

    expect(screen.getByText(/names north 2 times/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Use all' })).toBeTruthy()
  })

  it('is still offered when there is nothing wrong with it', () => {
    renderPanel({ warnings: [] })

    expect(screen.queryByText('Worth a look:')).toBeNull()
  })
})

describe('when it fails', () => {
  it('says why, and offers nothing to accept', () => {
    renderPanel({ status: 'failed', fields: [], error: 'The assistant is busy.' })

    expect(screen.getByRole('alert').textContent).toContain('The assistant is busy.')
    expect(screen.queryByRole('button', { name: 'Use all' })).toBeNull()
  })

  it('can be dismissed', () => {
    const handlers = renderPanel({ status: 'failed', fields: [], error: 'Nope.' })

    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }))

    expect(handlers.onDiscard).toHaveBeenCalledOnce()
  })
})

it('is invisible until asked', () => {
  const { container } = render(
    <DraftPanel
      status="idle"
      elapsed={0}
      fields={[]}
      warnings={[]}
      error={null}
      onUse={vi.fn()}
      onDiscard={vi.fn()}
    />,
  )

  expect(container.firstChild).toBeNull()
})
