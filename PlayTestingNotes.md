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


- noticed if I sign in on two different devices for the same account and character it can cause an issue with the SSE not being sent to both devices.
Options. You can be using the same account with different character at the same time, but not the same character. We may need to add something to the client to force kick a charceter out of the game. We need to track the most recent session for that character so we can replace it with a new one and remove all the old ones.