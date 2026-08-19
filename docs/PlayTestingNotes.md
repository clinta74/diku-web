I would expect the offer to have the link as part of the actual text. this may take re-authoring the offer. Then once the quest is given it should give the in progress text as that is the instructions on what to do. those are the instructsion a character would get again if talking to the NPC later to remind them what to do. 

Ilvaro's House
Exits: east
Deacon Pell of Ilvaro's house is here.
> talk pell
'Still up there, are they? They are not getting lighter.'
'They are holding things,' Pell says, and stops, and starts again. 'Somebody is missing those things. That is all I am confident about.'
(What Sulveth Keeps — 'talk house sulveth' to take it on.)
> talk house sulveth
'They are holding things,' Pell says, and stops, and starts again. 'Somebody is missing those things. That is all I am confident about.'
You take on What Sulveth Keeps.
> talk house
'Still up there, are they? They are not getting lighter.'
'Six should do. Take them to the Keeper, not to me. She will know what they are. I will only guess and be wrong in front of you.'

I would think the link should be on the things which means we have to add some sort of marker in the offer phrase to be able to author the text to include the links and trigger the right phrase to get the quest.

---

## Done: the link is in the sentence, and accepting tells you what to do

**The marker is `<angle brackets>` in the offer**, and the marked words are the keyword — the same
string, parsed back out, so there is nothing to keep in step. Not square brackets, which are what a
writer reaches for when inserting an aside and this prose is full of people trailing off; not
braces, which already mean *substitute a value* in the login greeting. Angle brackets appear
nowhere in the Reaches — the pipes and square brackets that turned up in a search were all
single-character item icons.

All 35 offers are re-authored. Pell now reads:

> 'They are holding things,' Pell says, and stops, and starts again. 'Somebody is missing those
> **things**. That is all I am confident about.'

Clicking *things* runs `talk pell things`, and the transcript shows that command — which is the
point. The label is a word and the command is a command, so unlike the old parenthetical they
differ on purpose: watching `talk pell things` appear is how the syntax gets learned.

**Accepting now says the in-progress line**, exactly as you asked — the same words the giver
repeats when you come back and ask again. The offer is the pitch and you have just read it. The one
exception is a chain step that starts itself off a turn-in: nobody pitched that one, so it still
speaks its offer, with the markers stripped, since an invitation to take on a quest already in your
journal is a lie.

**And the address in that transcript was wrong.** You typed `talk pell`; the game told you to type
`talk house`, because the address was the last word of the mob's name and Pell is named after a
building. It was six of the eight givers: `talk gates` for Vesh, who follows the gates, `talk
expelled` for Sister Aveth, `talk plates` for Bellic Vane. The name is now cut at the first word
that opens a describing clause — but the real fix is that **every command is round-tripped before
it is sent**, back through the same targeting and matching your own typing goes through, and only
rendered if it comes back holding the right mob and the right quest. A link that starts the wrong
quest is not a thing that can ship; the worst case is that it degrades to the old parenthetical.

That is also the answer to two quests on one table marking the same word: the word means neither,
so neither is linked, and the import refuses the content (`check-bundle` errors, demonstrated
failing before it was believed). No giver in the Reaches can currently offer two at once — every
multi-quest giver is a chain or a Path fan-out — so the rule is future-proofing, but Vesh's twenty
quests are exactly where it would first go wrong. 