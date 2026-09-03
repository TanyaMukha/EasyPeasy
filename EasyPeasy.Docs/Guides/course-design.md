# Course Design

How to build a module that actually teaches, rather than one that merely contains the material.
Audience is whoever is authoring course content — the decisions here are about sequencing and card
choice, not about the archive format ([course-archive-authoring.md](course-archive-authoring.md))
or the entry syntax ([entry-notation.md](entry-notation.md)).

## The distinction everything else follows from

**Recognition is not production.**

Recognizing *should have been rolled back* when you read it is one skill. Producing it, correctly
inflected, under time pressure, from a Ukrainian prompt, is a different one. Reading a lot of
material in the target language trains the first and barely touches the second.

This matters because assessment — placement tests, interviews, writing anything — tests production
almost exclusively. A module built entirely from multiple-choice cards can be answered at 100%
while leaving production untouched, and the gap stays invisible until something external measures
it.

So: **every module needs at least one card kind where the learner types the answer.**

## The effort ladder

The card kinds are not interchangeable. They sit at different points on a single axis — how much
the learner has to generate versus how much is handed to them:

| Card kind | Effort | What it trains |
|---|---|---|
| `StudyCard.Term`, `StudyCard.Text` | none | Exposure. No retrieval happens; this is reading |
| `TestCard.Matching` | low | Recognition, with all options visible at once |
| `TestCard.SingleChoice` / `MultipleChoice` | low | Recognition plus discrimination between options |
| `StudyCard.BlurredText` | medium | Cued recall — the answer is hidden, the context is not |
| `TestCard.Cloze` | high | Production, constrained by the surrounding sentence |
| `TestCard.ShortAnswer` | highest | Free production, no scaffolding |

Difficulty here is a feature, not a cost. The cards that feel hardest to answer are the ones that
move the material into production, and the ones that feel easy mostly confirm what is already
known.

The practical consequence: a module weighted toward the top of this table feels productive and
teaches little. Weight it toward `Cloze` and `ShortAnswer`, and use the recognition kinds as the
approach rather than the destination.

## Sequencing inside a module

Introduce, then recognize, then produce — in that order, for the same material:

1. **`Term` or `Text`** to introduce the rule or the vocabulary in context.
2. **`Matching` or `SingleChoice`** to establish the shape of it while the options are still
   visible.
3. **`Cloze`** to force production while the sentence still constrains the answer.
4. **`ShortAnswer`** for the items worth being able to produce cold.

Skipping straight to production produces failure without learning; stopping at recognition produces
learning that does not transfer. `BlurredText` is the useful bridge between steps 2 and 3 —
`Grouped` reveal mode for an expression split across the sentence (*breaks* … *down*), `Independent`
for a single span.

Mixing card kinds within one session beats grouping them by kind. Predictable blocks let the
learner answer from short-term memory rather than retrieving.

## Sizing

Existing vocabulary modules in the English for IT course run 11–38 entries; the grammar drills run
60–120 cloze cards on a single rule. Both work, for different reasons: a vocabulary module is one
sitting, a drill is a rule practised until it is automatic.

What does not work is a module that mixes twelve unrelated grammar points with thirty unrelated
words. One module, one thing to learn.

## Grammar: cover the whole ladder

Most grammar points are not single facts but graded series, and a module that covers only the
bottom rung leaves the learner unable to tell which rung they are actually on. Conditionals are the
clearest case:

| Structure | Level |
|---|---|
| `If you heat water, it boils` | A2 |
| `If it rains, I'll stay home` | A2/B1 |
| `unless`, `as long as`, `provided that` | B1 |
| `If I had time, I would help` | B1 |
| `If I had known, I would have told you` | B1+/B2 |
| `wish` / `if only` + past perfect | B2 |
| `If I had slept, I wouldn't be tired now` | B2 |
| `Had I known…`, `Should you need…` | B2/C1 |

A module with eight cards on the second rung and nothing above it produces confident use of one
form and no data about the rest. Build the ladder, then let the practice results say where it
breaks.

The same applies to prepositions by meaning, tense contrasts, and modal perfects — all of them
graded, all of them commonly taught as though they were flat.

## Near-synonyms are where the levels actually separate

Vocabulary that a dictionary translates identically but that native usage separates by register,
formality, or domain is the densest teaching material available, and it is what distinguishes B1
from B2 far more than raw word count.

`dodgy` / `questionable` / `doubtful` all translate as «сумнівний». Which one is correct depends on
whether the context is slang, a professional assessment, or an estimate of likelihood. A card that
puts all three among the options and a context that admits only one teaches something no
word-and-translation pair can.

Build these deliberately: put the plausible-but-wrong synonym in the options as a distractor, and
make the context — not the vocabulary — carry the decision. `EnglishForItB2Unit1TranslationTraps`
in `EasyPeasy.ContentTools` is the worked example.

## Hints

`Hint` is shown on demand and does not count as failure, which makes it the right home for anything
that would give the answer away if it were in the question:

- a full-sentence translation, for a cloze card where the sentence itself is the point
- the disambiguating context for a near-synonym card
- the rule being applied, for a grammar drill

`Note` is different: it is not shown during practice at all. Register, regional variation, and
"do not confuse with…" belong there — they are reference material for review, not scaffolding for
recall.

## What the app already does, so the course does not have to

Difficulty rating, review counts, selection filters and day streaks are per-card and independent of
the material's language. Practically this means the course does not need to encode a review
schedule: author the material once, and let the filters surface what is weak rather than
re-reading everything in order.

Tolerant answer comparison means an entry does not need every acceptable typing spelled out — that
is what `[]`, `/` and the `sb`/`sth` placeholders are for. Authoring five near-identical entries to
cover five phrasings is a sign the notation is being under-used.
