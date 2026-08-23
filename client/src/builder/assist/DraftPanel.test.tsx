// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { RoomDraft } from '../../net/builderApi'
import { DraftPanel } from './DraftPanel'

afterEach(cleanup)

const draft: RoomDraft = {
  title: 'The Tollhouse Steps',
  description: 'Stone steps, worn smooth and slick with moss.',
  exits: [],
}

function renderPanel(props: Partial<Parameters<typeof DraftPanel>[0]> = {}) {
  const handlers = {
    onUseTitle: vi.fn(),
    onUseDescription: vi.fn(),
    onUseBoth: vi.fn(),
    onDiscard: vi.fn(),
  }

  render(
    <DraftPanel
      status="ready"
      elapsed={0}
      draft={draft}
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
    renderPanel({ status: 'working', elapsed: 7, draft: null })

    expect(screen.getByRole('status').textContent).toContain('Drafting… 7s')
  })

  it('counts past a minute in minutes', () => {
    renderPanel({ status: 'working', elapsed: 95, draft: null })

    expect(screen.getByRole('status').textContent).toContain('1m 35s')
  })

  /**
   * The explanation arrives late on purpose. Said immediately it is noise on a fast draft; said
   * never, a builder twenty seconds in concludes it has hung.
   */
  it('explains the wait only once the wait is odd', () => {
    renderPanel({ status: 'working', elapsed: 5, draft: null })
    expect(screen.queryByText(/takes a few minutes/)).toBeNull()

    cleanup()

    renderPanel({ status: 'working', elapsed: 25, draft: null })
    expect(screen.getByText(/takes a few minutes/)).toBeTruthy()
  })

  it('offers nothing to accept while there is nothing to accept', () => {
    renderPanel({ status: 'working', elapsed: 5, draft: null })

    expect(screen.queryByRole('button', { name: 'Use both' })).toBeNull()
  })
})

describe('when it has something', () => {
  it('shows the draft without applying it', () => {
    const handlers = renderPanel()

    expect(screen.getByText('The Tollhouse Steps')).toBeTruthy()
    expect(screen.getByText(/worn smooth/)).toBeTruthy()

    // Nothing has been called: showing a draft is not taking it.
    expect(handlers.onUseBoth).not.toHaveBeenCalled()
    expect(handlers.onUseTitle).not.toHaveBeenCalled()
    expect(handlers.onUseDescription).not.toHaveBeenCalled()
  })

  /**
   * Title and description are taken separately because they fail separately - the prose is often
   * worth keeping when the title is not. Forcing both is what turns a useful suggestion into one
   * people stop pressing.
   */
  it.each([
    ['Use both', 'onUseBoth'],
    ['Title only', 'onUseTitle'],
    ['Description only', 'onUseDescription'],
    ['Discard', 'onDiscard'],
  ] as const)('%s calls %s', (label, handler) => {
    const handlers = renderPanel()

    fireEvent.click(screen.getByRole('button', { name: label }))

    expect(handlers[handler]).toHaveBeenCalledOnce()
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
    expect(screen.getByRole('button', { name: 'Use both' })).toBeTruthy()
  })

  it('is still offered when there is nothing wrong with it', () => {
    renderPanel({ warnings: [] })

    expect(screen.queryByText('Worth a look:')).toBeNull()
  })
})

describe('when it fails', () => {
  it('says why, and offers nothing to accept', () => {
    renderPanel({ status: 'failed', draft: null, error: 'The assistant is busy.' })

    expect(screen.getByRole('alert').textContent).toContain('The assistant is busy.')
    expect(screen.queryByRole('button', { name: 'Use both' })).toBeNull()
  })

  it('can be dismissed', () => {
    const handlers = renderPanel({ status: 'failed', draft: null, error: 'Nope.' })

    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }))

    expect(handlers.onDiscard).toHaveBeenCalledOnce()
  })
})

it('is invisible until asked', () => {
  const { container } = render(
    <DraftPanel
      status="idle"
      elapsed={0}
      draft={null}
      warnings={[]}
      error={null}
      onUseTitle={vi.fn()}
      onUseDescription={vi.fn()}
      onUseBoth={vi.fn()}
      onDiscard={vi.fn()}
    />,
  )

  expect(container.firstChild).toBeNull()
})
