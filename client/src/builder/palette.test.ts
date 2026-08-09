import { describe, expect, it } from 'vitest'
import { PALETTE, PALETTE_GLYPHS, tileFor } from './palette'

/**
 * The palette is map art's vocabulary, so its failure modes are the ones that make art
 * undrawable rather than ugly: a glyph the engine cannot index, or the same glyph offered twice
 * meaning two different things.
 */
describe('grid painter palette', () => {
  it('offers every glyph exactly once', () => {
    // A duplicate would appear in two groups with two default tile names, and which one you got
    // would depend on which swatch you happened to click.
    const seen = PALETTE_GLYPHS.map(([g]) => g)
    expect(seen.length).toBe(new Set(seen).size)
  })

  it('offers only single-cell characters', () => {
    // The painter writes a glyph into a row by index and the engine reads terrain back per
    // char. A surrogate pair — any emoji — would be split across two cells and neither half
    // would draw.
    for (const [glyph] of PALETTE_GLYPHS) {
      expect(glyph).toHaveLength(1)
      expect(glyph.codePointAt(0)!).toBeLessThanOrEqual(0xffff)
    }
  })

  it('names a tile for every glyph', () => {
    for (const [glyph, tile] of PALETTE_GLYPHS) {
      expect(tile.trim()).not.toBe('')
      expect(tileFor(glyph)).toBe(tile)
    }
  })

  it('falls back to floor for a glyph it does not know', () => {
    // Art can carry glyphs the palette never offered; painting must not crash on one.
    expect(tileFor('§')).toBe('floor')
  })

  it('gives every group a label and at least one glyph', () => {
    for (const group of PALETTE) {
      expect(group.label.trim()).not.toBe('')
      expect(group.glyphs.length).toBeGreaterThan(0)
    }
  })

  it('carries the box-drawing characters walls are built from', () => {
    const glyphs = new Set(PALETTE_GLYPHS.map(([g]) => g))

    // Single and double line, all four corners and all four tees of each.
    for (const g of '─│┌┐└┘├┤┬┴┼') expect(glyphs.has(g)).toBe(true)
    for (const g of '═║╔╗╚╝╠╣╦╩╬╪╫') expect(glyphs.has(g)).toBe(true)
  })

  it('carries blocks, stairs, and furniture', () => {
    const glyphs = new Set(PALETTE_GLYPHS.map(([g]) => g))

    for (const g of '█▓▀▌▐') expect(glyphs.has(g)).toBe(true)
    for (const g of '≡▬▲▄◎†♠♣⌐') expect(glyphs.has(g)).toBe(true)
  })

  it('keeps the original ASCII glyphs, so existing art stays paintable', () => {
    const glyphs = new Set(PALETTE_GLYPHS.map(([g]) => g))

    for (const g of '.#~,"+o^') expect(glyphs.has(g)).toBe(true)
  })
})
