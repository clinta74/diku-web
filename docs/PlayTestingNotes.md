# Play testing notes

Add anything noticed while playing here. Cleared as items are done.

_Nothing outstanding._ The queue lives in [BUGS.md](BUGS.md).

All three notes are built, as Phase 5.4 — the reasoning is in §4.13 (shop pricing) and §4.14
(reading your pack).

**Shopkeepers can be dearer than the price list.** A `markup` on the mob: `0.1` is 1.1×, rounded
up to the next whole gold, never adding less than a gold. So the loaf that used to cost 1 now costs
2, which is the case that made the note worth writing — rounding to nearest would have left a
village baker's markup doing nothing at all. It moves `list` and `buy` together and leaves sellback
alone, so a shop that is expensive to buy from is not automatically one that pays well. The mob
editor shows the asking price against each stocked item with the base value beside it, so the dial
is readable without walking into the shop.

One thing the build found that the note had not: **`buy` was pricing from the first shopkeeper in
the room rather than from the one that stocks the item.** With one price in the world that was
invisible; with a markup it would have sold the baker's bread at the smith's rates. It is the same
shape as the bug that once made a second shop's stock unbuyable, one layer down.

**The pack reads at a glance now.** Quest items are marked `(q)` rather than `(quest)` — a single
letter reads as a margin note, which is what it is, and `examine` still says the whole sentence.
Duplicates collapse to one line with a count: `stone (x3)`, and `(x2 q)` when they are also quest
items, one bracket carrying both because two in a row would read as two separate facts. Quest-bound
copies count apart from ordinary ones, since the tag is a statement about what you can *do* with the
thing. Worn items never collapse — they are listed under their slots and the slot is the
information.

The collapse is display only, and that is the assertion that matters: `drop stone` against three
stones still drops one.


**Two devices on one character now hand over cleanly.** You were right about the cause and about
the fix. It was not a race: a session's event channel is `SingleReader`, so two streams reading it
get roughly half the events each, every time — which is why neither screen showed a whole fight.

The newest connection holds the stream, and the older one is *told* rather than just cut off: it
gets a `sys` event of kind `displaced`, closes, stops retrying, and goes back to character select
with the reason on screen. It is not offered the character back — two screens that can each reclaim
it is the same tug-of-war in a politer costume, so the older one is simply finished. Picking the
character off the list again is how you take it back, which is one click and is also an honest
description of what it does.

**The first version of this fix was wrong in the way you saw**, and worth writing down. Ownership
of the stream lived on the session — but *entering* builds a new session, so the moment the second
device arrived, everything the server knew about the first device was thrown away. The first
device's stream simply went quiet, which from a browser is indistinguishable from a dropped
network, so it reconnected, met a session that had never heard of it, was served, and took the
character back. Then the second device got the takeover notice, and the two swapped roles for ever.

Two changes fixed it. Ownership now lives in the session registry keyed by **character**, which
outlives every session that character has. And the hand-over happens at `enter` rather than when
the old stream notices it has gone quiet: entering is the act that decides which device is playing
the character, so the old screen is told before the new one has even connected. There is nothing
left for it to race.

**A second thing went wrong on the way, and it is the more useful lesson.** The screenshot showed
the device that had just *won* the character sitting on "Disconnected. Trying to reconnect…" —
which had nothing to do with sessions at all. The takeover callback had been added to the stream
effect's dependency list, and the parent passes it inline, so it was a new function on every render
of `App` — and `App` re-renders on every room change. The stream was being torn down and reopened
continuously, and each teardown reports a dropped connection, which marks the character link-dead
and completes its event channel; the stream that replaced it then read a closed one.

`App` already carried a comment about this for `onRoomChange` — *"Stable so GameScreen's effect
does not re-fire on every render"* — and it did not stop it happening again. The handler is held in
a ref now, so the effect cannot depend on its identity whatever a caller passes. There is a test
that counts stream opens across three re-renders, and it fails loudly against the old code.

Being displaced is deliberately not the same as going link-dead — the character is standing there
being played on the other screen, so no grace window starts and the room is not told they have gone
still. The displaced screen also does not `leave` on its way out, which would have pulled the
character out from under the device that just took it.

Different characters on one account are untouched: they always had their own sessions, and still do.

One thing found on the way, left alone because it is a different mechanism: going link-dead
completes the session's channel, so `EventSource`'s automatic retry reconnects to a closed one and
stays silent. Only entering again re-establishes output, which is what the Rejoin button does — the
automatic retry recovers a *stream* but not a link-dead *character*. Worth deciding on separately.

- let's create a UX evaulation. I want to look for best practices, style consistancy, letter casing and spacing, layout spacing, resizablity, and intuitive.
I don't think we need the Follow my character aldenmoor.millbrook.tavern-common checkbox it can always be enabled.

- mobs templates should have a default for the wandering configuration and which should be not wander as the default for the template. This should still be control in the end by the mob spawner.

- the ability cool downs are a bit to quick. it would be nice to have something show your pending cool downs.