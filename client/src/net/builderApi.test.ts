import { expect, it } from 'vitest'
import { builderApi } from './builderApi'

/**
 * Where a bundle is downloaded from.
 *
 * Worth its own test because the setup panel's smoke test mocks `exportUrl` — so the query string
 * this builds, which is the whole of what the scope means over the wire, is asserted nowhere else.
 */

it('exports everything when given no scope', () => {
  expect(builderApi.exportUrl()).toBe('/api/builder/export')
  expect(builderApi.exportUrl({})).toBe('/api/builder/export')
})

it('narrows to a world or a zone, and a zone wins', () => {
  expect(builderApi.exportUrl({ world: 'ossara' })).toBe('/api/builder/export?world=ossara')
  expect(builderApi.exportUrl({ zone: 'ossara.gatetown' })).toBe(
    '/api/builder/export?zone=ossara.gatetown',
  )

  // The hint under the World box says the zone wins, so it has to.
  expect(builderApi.exportUrl({ world: 'ossara', zone: 'ossara.gatetown' })).toBe(
    '/api/builder/export?zone=ossara.gatetown',
  )
})

it('takes the abilities on their own, over any place', () => {
  // An ability belongs to a Path and not to a place, so there is nothing for a zone to narrow —
  // and a request that carried both would be asking two questions at once.
  expect(builderApi.exportUrl({ only: 'abilities' })).toBe('/api/builder/export?only=abilities')
  expect(builderApi.exportUrl({ only: 'abilities', zone: 'ossara.gatetown' })).toBe(
    '/api/builder/export?only=abilities',
  )
})
