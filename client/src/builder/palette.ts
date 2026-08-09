/**
 * The glyphs the grid painter offers, grouped so a builder can find a wall corner without
 * hunting through a character map.
 *
 * Every glyph must be a single BMP character. The painter writes one into a row by index and
 * `RoomLayoutService` reads terrain back per `char`, so a surrogate pair — any emoji — would be
 * split across two cells and neither half would draw. Box-drawing, block, and geometric shapes
 * are all exactly one cell wide in a monospace font, which is what makes them usable as map art.
 *
 * The tile name beside each glyph is only a *default*: it seeds the room's legend the first time
 * the glyph is painted, and the legend editor renames it per room. That is why the same glyph can
 * be a wall in one room and a bar in another without the palette taking a position.
 */
export interface PaletteGroup {
  label: string
  hint?: string
  glyphs: [glyph: string, tile: string][]
}

export const PALETTE: PaletteGroup[] = [
  {
    label: 'Ground',
    glyphs: [
      ['.', 'floor'],
      ['·', 'path'],
      [',', 'grass'],
      ['"', 'undergrowth'],
      ['░', 'rubble'],
      ['▒', 'gravel'],
      ['≈', 'water'],
      ['~', 'water'],
      ['≡', 'stairs'],
    ],
  },
  {
    label: 'Walls',
    hint: 'Single line. Corners and tees join up where they meet.',
    glyphs: [
      ['─', 'wall'],
      ['│', 'wall'],
      ['┌', 'wall'],
      ['┐', 'wall'],
      ['└', 'wall'],
      ['┘', 'wall'],
      ['├', 'wall'],
      ['┤', 'wall'],
      ['┬', 'wall'],
      ['┴', 'wall'],
      ['┼', 'wall'],
      ['#', 'wall'],
    ],
  },
  {
    label: 'Heavy walls',
    hint: 'Double line, for keeps and outer walls.',
    glyphs: [
      ['═', 'wall'],
      ['║', 'wall'],
      ['╔', 'wall'],
      ['╗', 'wall'],
      ['╚', 'wall'],
      ['╝', 'wall'],
      ['╠', 'wall'],
      ['╣', 'wall'],
      ['╦', 'wall'],
      ['╩', 'wall'],
      ['╬', 'wall'],
      ['╪', 'wall'],
      ['╫', 'wall'],
    ],
  },
  {
    label: 'Solid',
    hint: 'Blocks and half-blocks, for rock faces and low ledges.',
    glyphs: [
      ['█', 'rock'],
      ['▓', 'rock'],
      ['▀', 'ledge'],
      ['▌', 'ledge'],
      ['▐', 'ledge'],
    ],
  },
  {
    label: 'Ways through',
    glyphs: [
      ['╥', 'gate'],
      ['╨', 'gate'],
      ['╞', 'gate'],
      ['╡', 'gate'],
      ['+', 'door'],
      ['⌐', 'counter'],
    ],
  },
  {
    label: 'Furniture',
    glyphs: [
      ['▬', 'table'],
      ['▲', 'forge'],
      ['▄', 'anvil'],
      ['■', 'chest'],
      ['◎', 'well'],
      ['†', 'altar'],
      ['♠', 'tree'],
      ['♣', 'shrub'],
      ['o', 'prop'],
      ['^', 'stairs'],
    ],
  },
]

/** Every glyph in the palette, flattened — for lookups and for tests. */
export const PALETTE_GLYPHS: [string, string][] = PALETTE.flatMap((group) => group.glyphs)

/** The default tile name for a glyph, or 'floor' when the palette does not know it. */
export function tileFor(glyph: string): string {
  return PALETTE_GLYPHS.find(([g]) => g === glyph)?.[1] ?? 'floor'
}
