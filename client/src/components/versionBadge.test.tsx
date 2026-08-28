// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { VersionBadge } from './VersionBadge'

/**
 * The build the server is running, shown where somebody can read it.
 *
 * It exists because the question had no answer short of `docker inspect` on the NAS: the OCI
 * labels are correct and unreachable, and TrueNAS dropped third-party catalogues in 24.10, so a
 * custom app has no version listing at all.
 */
const version = (body: unknown, ok = true) =>
  vi.fn(() =>
    Promise.resolve(
      new Response(ok ? JSON.stringify(body) : null, {
        status: ok ? 200 : 503,
        headers: { 'Content-Type': 'application/json' },
      }),
    ),
  )

beforeEach(() => {
  vi.unstubAllGlobals()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

it('shows a tagged release as a version', async () => {
  vi.stubGlobal(
    'fetch',
    version({ version: '1.0.0', revision: 'abc1234def', shortRevision: 'abc1234' }),
  )

  render(<VersionBadge />)

  expect(await screen.findByText('v1.0.0')).toBeTruthy()
})

it('shows the commit when the build is not a release', async () => {
  // A branch build has no version to report. Showing "v0.0.0" would read as a real release of a
  // very early one, which is worse than saying nothing - so it shows the commit instead.
  vi.stubGlobal(
    'fetch',
    version({ version: '0.0.0', revision: 'ff2a2da9911', shortRevision: 'ff2a2da' }),
  )

  render(<VersionBadge />)

  expect(await screen.findByText('ff2a2da')).toBeTruthy()
  expect(screen.queryByText('v0.0.0')).toBeNull()
})

it('carries the full revision as a title, so it can be copied', async () => {
  vi.stubGlobal(
    'fetch',
    version({ version: '1.0.0', revision: 'abc1234def5678', shortRevision: 'abc1234' }),
  )

  render(<VersionBadge />)

  const label = await screen.findByText('v1.0.0')
  expect(label.getAttribute('title')).toBe('abc1234def5678')
})

it('renders nothing at all when the server will not say', async () => {
  // A version line is the least important thing on the screen. A server too unwell to answer has
  // already told the user in a way that matters more, so this must not add an error of its own.
  vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new Error('offline'))))

  const { container } = render(<VersionBadge />)

  await waitFor(() => expect(container.textContent).toBe(''))
})
