import { afterEach, describe, expect, it, vi } from 'vitest'
import { builderApi } from '../../net/builderApi'

/**
 * What the exit PUT puts on the wire (PLAN.md §4.15).
 *
 * A PUT states the whole exit, so a condition the body omits is a condition the exit does not
 * have. That is what lets a lock come off — and it is also the failure worth a test of its own: a
 * client that quietly stopped sending the fields would not error, it would unlock every gate a
 * builder touched, and nothing about the request would look wrong.
 */

interface CapturedBody {
  to: string
  reciprocal: boolean
  requiredFlagKey: string | null
  requiredItemKey: string | null
  refusalMessage: string | null
  reciprocalConditions: boolean
}

function captureFetch() {
  // Declared with fetch's own parameters, not as a nullary function. Without them vi.fn types the
  // recorded calls as an empty tuple, so reading the request init back out of them does not
  // compile - which is what this whole helper exists to do.
  const fetchMock = vi.fn(
    async (_input: RequestInfo | URL, _init?: RequestInit) =>
      new Response(JSON.stringify({ key: 'w.z.r', exits: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
  )

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const bodyOf = (fetchMock: ReturnType<typeof captureFetch>): CapturedBody => {
  // Asserted rather than indexed blind: the mock's call tuple is typed as possibly empty, and
  // reading [0][1] from it does not compile. A failure here should also say "nothing was sent"
  // rather than throw on undefined three lines later.
  const [call] = fetchMock.mock.calls
  if (!call) throw new Error('setExit sent no request.')

  const init = call[1]
  if (!init?.body) throw new Error('setExit sent a request with no body.')

  return JSON.parse(String(init.body)) as CapturedBody
}

afterEach(() => vi.unstubAllGlobals())

describe('setExit', () => {
  it('sends every condition it was given', async () => {
    const fetchMock = captureFetch()

    await builderApi.setExit('w.z.r', 'north', 'w.z.n', false, {
      requiredFlagKey: 'attuned.grask',
      requiredItemKey: 'brass-key',
      refusalMessage: 'The gate does not know you.',
    })

    const body = bodyOf(fetchMock)
    expect(body.requiredFlagKey).toBe('attuned.grask')
    expect(body.requiredItemKey).toBe('brass-key')
    expect(body.refusalMessage).toBe('The gate does not know you.')
  })

  it('sends explicit nulls when given no conditions, rather than omitting them', async () => {
    // Omitting the keys would read on the server as "leave them alone", and a lock could never be
    // removed through the editor. Null is the whole mechanism for taking one off.
    const fetchMock = captureFetch()

    await builderApi.setExit('w.z.r', 'north', 'w.z.n')

    const body = bodyOf(fetchMock)
    expect(body).toHaveProperty('requiredFlagKey', null)
    expect(body).toHaveProperty('requiredItemKey', null)
    expect(body).toHaveProperty('refusalMessage', null)
  })

  it('does not mirror conditions to the return exit unless asked', async () => {
    // You can always leave a vault. Reciprocal topology defaults on; reciprocal locks do not.
    const fetchMock = captureFetch()

    await builderApi.setExit('w.z.r', 'north', 'w.z.n', true, {
      requiredFlagKey: 'attuned.grask',
      requiredItemKey: null,
      refusalMessage: null,
    })

    const body = bodyOf(fetchMock)
    expect(body.reciprocal).toBe(true)
    expect(body.reciprocalConditions).toBe(false)
  })

  it('mirrors them when asked', async () => {
    const fetchMock = captureFetch()

    await builderApi.setExit('w.z.r', 'north', 'w.z.n', true, {
      requiredFlagKey: 'attuned.grask',
      requiredItemKey: null,
      refusalMessage: null,
      reciprocalConditions: true,
    })

    expect(bodyOf(fetchMock).reciprocalConditions).toBe(true)
  })
})
