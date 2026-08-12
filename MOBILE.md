# Mobile client plan

**Status: proposal.** Nothing here is built. Findings were read out of `client/src` and
`src/DikuWeb.Engine` at commit `1a5fe83`; line references are from that commit and will drift.

A MUD is the most phone-shaped game there is — a column of text and one line of input. The current
client is not phone-shaped at all: it is a five-panel desktop grid pinned to `100vh`, driven by a
keyboard that a phone does not have. This is what closing that gap costs, in the order the work
wants to happen.

Estimates are solo-developer days including tests: **6–9 days to playable**, **10–15** with the
builder.

---

## 1. One responsive client, not a second app

Nothing here argues for a separate mobile codebase. The game screen is five panels over one SSE
stream (§5, §3.2); what differs on a phone is which panels are visible and how a command is sent,
and both are layout and input concerns. A second client would double the surface that has to stay
in step with the protocol, to solve a problem that is genuinely CSS and two components.

| Area | Target |
| --- | --- |
| Game | Phone-first. This is the part that pays for the work. |
| Auth and characters | Nearly there. The `.shell` screens are one centred column; they need touch targets and little else. |
| Builder | All the way down, once the zone canvas stops being permanently on screen (§5). |
| Install | A web app manifest, late. Standalone display is what finally makes the viewport behave. |

---

## 2. Starting position

Worth stating, because it changes the size of the job.

- `index.html` already carries a correct `viewport` meta, so the page is not rendered at desktop
  width and scaled down.
- Two breakpoints exist — the game stacks below 900px, the builder collapses below 1100px. Neither
  is a phone layout, but the intent is in the file.
- **Tapping is already a way to play.** Every entry in the room's contents list is a button that
  types its keyword into the input (§5). The mechanic a touch UI needs most is shipped.
- The ASCII map is 21×9 characters (`RoomLayoutService`), about 180px wide at any readable
  monospace size. It fits a phone without redesign.
- The stream reconnects itself: `EventSource` replays `Last-Event-ID` against the server's ring
  buffer (§3.4), which is what a flaky mobile network needs.

**One divergence to note.** §5 already says the map "collapses to a toggle so the text still
dominates on mobile". That was never built — the 900px breakpoint caps the map at 12rem and leaves
it on screen. The plan was right the first time; §4 below is that toggle, finally.

---

## 3. What breaks on a phone

Severity is about play, not tidiness. **Blocks** means you cannot reasonably play, **degrades**
means you can but it is unpleasant, **polish** is the rest.

| What happens | Where | Severity | Fix |
| --- | --- | --- | --- |
| **The input bar sits under the keyboard.** `.game` is `height: 100vh`, which on iOS counts browser chrome it cannot see and ignores the keyboard entirely. The two bottom rows end up off screen the moment you tap to type. | `App.css:242` | blocks | `100dvh`, `interactive-widget=resizes-content`, VisualViewport listener for Safari |
| **The map and room panel eat the screen.** Below 900px the layout stacks room, then map (capped at 12rem), then scrollback. On a 844px-tall phone the transcript gets what is left. | `App.css:529` | blocks | A phone breakpoint where the scrollback owns the screen |
| **Movement means typing.** `north`, `north`, `east` on a phone keyboard, for a game whose main verb is walking. There is no direction control anywhere. | `GameScreen.tsx` | blocks | An exit pad built from `state.room.exits`, which the client already holds |
| **iOS zooms in on focus.** Body type is 14px; Safari zooms any field under 16px and does not zoom back out. | `App.css:38` | blocks | 16px minimum on the command input under `(pointer: coarse)` |
| **Autocapitalise rewrites commands.** The input sets `spellCheck` and `autoComplete` but not `autoCapitalize`, `autoCorrect`, or `enterKeyHint`, so a phone offers "Go" instead of "Send" and sends `North`. | `GameScreen.tsx:443` | degrades | Four attributes |
| **The keyboard opens on arrival.** `autoFocus` costs half the screen before the player has seen the room. | `GameScreen.tsx:446` | degrades | No autofocus on coarse pointers; the exit pad is the primary control there |
| **History and completion are keyboard-only.** ↑/↓ walks history, Tab cycles completions. Neither key exists on a phone. | `GameScreen.tsx:382` | degrades | Recent-command chips; tapping contents already covers most of what Tab is for |
| **Touch targets are text-sized.** Contents entries are `padding: 0.1rem 0`, the legend is 0.8rem, the exits line is plain text. All well under the ~44px a thumb needs. | `App.css:287` | degrades | A coarse-pointer block that raises hit areas without changing the desktop look |
| **Backgrounding drops you out of the world.** Ninety seconds away and the link-dead sweep removes the character; coming back lands on the character screen. | `GameLoop.cs:619` | degrades | Longer grace, resume-triggered reconnect, one-tap re-entry (§6) |
| **Panning the zone canvas needs a modifier.** Ctrl-drag to pan, Shift+click to link. A phone has neither key — and a desktop builder should not need them either. | `ZoneCanvas.tsx:84` | degrades | Plain drag-to-pan on Pointer Events (§5) |
| **The grid painter paints on mouse-down only.** Same cause, and drag-to-paint is the whole point of the control. | `GridPainter.tsx:130` | degrades | Pointer Events, shared with the canvas fix |
| **Nothing accounts for the notch or the home bar.** No `viewport-fit=cover`, no `env(safe-area-inset-*)`, so the vitals row lands under the gesture bar. | `index.html` | polish | One meta change and safe-area padding |

---

## 4. Where the screen goes

One decision drives the whole phone layout: **the transcript is the game, so the transcript gets the
screen.** Everything else earns its place by being one tap away rather than permanently visible.

```
┌─────────────────────────────┐   Header strip: room title, sheet
│ The North Gate    ▤ room  ● │   toggle, connection dot
├─────────────────────────────┤
│ > north                     │
│ You go north, under the     │
│ arch.                       │
│ Mira arrives from the south.│   Scrollback takes everything
│ Mira says, "Careful past    │   left over — the only panel
│ the gate."                  │   that grows
│ > attack wolf               │
│ You strike the grey wolf.   │
│ The grey wolf bites you.    │
├─────────────────────────────┤
│  N    E    S    w    ↑    ↓ │   Exit pad, from room.exits;
├─────────────────────────────┤   absent directions greyed, not
│ > say, look, attack…        │   hidden, so it never reflows
├─────────────────────────────┤   under the thumb
│ HP 38/42  FO 30  ST 26      │
└─────────────────────────────┘
```

The room panel and the map live behind the `▤ room` toggle, sliding up over the transcript and
closing back to where you were. That is the §5 toggle that was specified and never built.

### What replaces the keyboard

Three controls cover most of what a player does, and each derives from state the client already
holds — none of this needs a protocol change.

- **The exit pad** sends a direction per tap, from `state.room.exits`. Directions the room does not
  have render disabled rather than hidden, so the pad does not reflow under the thumb on every move.
- **Tap-to-target, made bigger.** The contents list already inserts a keyword into the input. On
  touch it should go one step further: a tap opens a small verb menu — look, attack, talk, get —
  using the dropdown primitive already in `package.json`.
- **Recent commands as chips** above the input, over the same per-character history that ↑ walks on
  desktop. Same data, an affordance a thumb can reach.

**Leave the type-anywhere handler alone.** Redirecting stray keystrokes into the command box is a
desktop feature and should stay one. It is already gated behind `active`; it just needs to not run
on coarse pointers, where there are no stray keystrokes to catch.

---

## 5. The builder: the canvas is the only desktop-shaped part

The first draft of this plan proposed blocking the builder below 768px, on the grounds that a
three-pane world editor cannot work on a phone. That was the wrong conclusion from the right
observation. Almost all of the builder is forms — text fields, checkboxes, selects, lists — and
forms are the one thing phones have always been good at. The part that genuinely needs a large
pointer-driven surface is the **zone canvas**, and it does not have to be on screen to have a
builder.

### Drag to pan, with no modifier, on every device

`ZoneCanvas` requires Ctrl to pan because a plain drag used to fight with selecting rooms — the
reasoning is in the comment above the component. But `handlePanStart` already bails when the drag
begins on a `.room-box`, so the collision that remains is narrower than it looks: a click that
drifts a few pixels. A **movement threshold** — under ~4px it is a click, over it is a pan —
settles that without asking for a key.

The same handler written on Pointer Events serves mouse, touch, and pen at once, so **removing the
modifier and supporting touch are one piece of work rather than two**. `GridPainter` gets the same
treatment for the same reason.

Shift+click becomes a **link mode**: a toggle in the canvas header, on for as long as it takes to
pick two rooms. The interaction already half-works this way — the canvas keeps `linkFrom` state and
shows a "linking from… / cancel" banner. The toggle only gives that mode a way in that is not a
held key.

### On a phone, the canvas is summoned

Not rendered at all until asked for. The world tab shows its tree and the room editor; the map opens
over them as a full-screen overlay from a control in the tab header, and closes back to where you
were. Cheaper than making a canvas share a 390px screen, and better — a map you summon gets the
whole screen when you have it.

**What this changes downstream.** The builder no longer needs a width floor. With the canvas on
demand, the remaining work is a pane collapse and touch-sized controls: ordinary responsive work
rather than a rebuild. It also means the canvas fix is not mobile-only work paid for by the mobile
budget — plain drag-to-pan is a desktop improvement that happens to be the whole of the touch story.

---

## 6. Surviving being backgrounded

A phone suspends background tabs. Switch to a message and back, and the stream has been dead for the
whole time you were away — fine for a minute, not fine at ninety seconds, because that is when the
link-dead sweep removes the character from the world. On a desktop that window is generous. On a
phone it is a normal interruption.

- A `visibilitychange` handler that reconnects on resume rather than waiting out `EventSource`'s own
  retry timer.
- A re-entry path: when the character has been dropped, offer to walk back in from where the player
  is, instead of returning them to the character list.
- A longer grace window — which needs a server change first.

> **Found while checking this.** `docker-compose.prod.yml` sets `Engine__LinkDeadGraceSeconds` and
> `Engine__StartingRoom`, but `Program.cs` configures the engine options in code and never binds the
> `Engine` configuration section. Both environment variables are currently no-ops, and the real
> grace window is the hardcoded default — `LinkDeadGracePulses = 360` at 250ms per pulse, so 90
> seconds. Worth fixing regardless of mobile; it is also the knob this section wants to turn.

---

## 7. Phases

Ordered so each phase leaves the app better than it found it, and so the first two are worth
shipping alone.

### M0 — Stop the bleeding · 0.5–1 day

- [ ] `100dvh` with a `100vh` fallback, plus `viewport-fit=cover` and
      `interactive-widget=resizes-content`
- [ ] Safe-area padding on the input and vitals rows
- [ ] 16px command input, and the four missing input attributes
- [ ] No autofocus and no type-anywhere under `(pointer: coarse)`

Nothing structural. After this the game is usable on a phone the way a badly-fitting shirt is
wearable.

### M1 — The phone layout · 2–3 days

- [ ] A ≤600px breakpoint: header strip, transcript, input, vitals, and nothing else competing for
      height
- [ ] Room panel and map into a slide-over sheet, toggled from the header (the §5 toggle)
- [ ] Input pinned above the keyboard via VisualViewport
- [ ] Coarse-pointer hit areas for contents, legend, and tabs

### M2 — Touch verbs · 2–3 days

- [ ] Exit pad from the room payload
- [ ] Verb menu on a contents tap
- [ ] Recent-command chips over the existing history store

The phase that makes it a game you would *choose* to play on a phone rather than one you can.

### M3 — Survive being backgrounded · 1–2 days

- [ ] Bind the `Engine` configuration section so the grace window is configurable at all
- [ ] Reconnect on resume; re-entry offer when the character was dropped
- [ ] Connection state in the header strip, not only in the vitals row

### M4a — Canvas without modifiers · 2–3 days

- [ ] Pointer Events for `ZoneCanvas` and `GridPainter`, replacing the mouse handlers
- [ ] Plain drag-to-pan behind a ~4px threshold; the Ctrl requirement and its keyboard listener come
      out
- [ ] Link-mode toggle in the canvas header, replacing Shift+click
- [ ] Pinch-zoom, which the current canvas has no equivalent of at all

Ships on its own as a desktop improvement, with or without the rest of this plan. Touch support
falls out of it rather than being added to it.

### M4b — Builder on small screens · 2–3 days

- [ ] Three panes to two below ~1100px, two to one below ~768px, tree as a drawer rather than a
      stacked column
- [ ] Zone canvas behind a control in the world tab header on phones, opening as a full-screen
      overlay
- [ ] Touch-sized controls through the editors — tree rows, flag lists, and sub-tabs are all
      text-sized today

### M5 — Installable · 0.5–1 day

- [ ] Manifest, icons, theme colour, Apple meta tags
- [ ] Standalone display, which removes the browser chrome that makes viewport height awkward in the
      first place
- [ ] A service worker only if the Android install prompt is wanted; there is no offline story to
      tell

---

## 8. Testing

The existing suite is jsdom under Vitest (§9), which can prove logic and cannot prove layout.

- **Pure functions first.** Exit-pad derivation, layout-mode selection, and the history chip list are
  all pure and testable exactly as `scrollFollow` and `completion` already are.
- **Playwright with device emulation** for three flows — sign in, enter the world, walk two rooms —
  on one iOS and one Android profile. This is the first e2e tooling in the repo, so budget half a day
  for the harness itself.

Emulation will not catch the keyboard behaviour, which is the thing most likely to be wrong. That
needs one real device each way, once per phase.

---

## 9. Open questions

1. **Is pinch-zoom in scope for the canvas?** Once it is on Pointer Events, two-finger zoom is a
   modest addition — and there is no zoom on desktop today either, so it is a new capability rather
   than parity. It is also the easiest thing to get subtly wrong.
   *Recommend: yes, in M4a, while the pointer plumbing is open.*
2. **How long should a backgrounded phone keep its character?** Ninety seconds today. Five minutes
   covers a normal interruption; it also means a genuinely disconnected player lingers that much
   longer. *Recommend: five minutes, once the setting actually binds.*
3. **Does the exit pad stay visible on desktop?** Useful there too, but it competes with the
   transcript and duplicates typing that desktop players already do faster.
   *Recommend: coarse pointers only, at least to begin with.*
4. **Install now or later?** The manifest is cheap and makes the viewport problems easier, which
   argues for early — but it also puts an icon on a home screen before the thing behind it is good.
   *Recommend: after M2.*
