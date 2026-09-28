# EasyPeasy

A personal, offline study app in which the learner is also the author of the material.
It is not a course with ready-made content and not a client for someone else's service:
everything lives in a local SQLite database on the device, with no account, no sign-up and
no network.

.NET 9 MAUI Blazor Hybrid, with Ukrainian as the interface language.

It is built and used on **Windows and Android** — those are the two platforms every feature is
tried on. The project also carries the `net9.0-ios` and `net9.0-maccatalyst` targets that come
with any MAUI project, but nothing has ever been built or run there and that is not planned:
treat them as untested, not as supported platforms.

## What it does

**Keeps your own library.** Subject → course → module, and four kinds of content inside a module:

| Content | What it holds |
|---|---|
| Words and phrases | spelling, transcription, translation, note, recorded pronunciation, example sentences |
| Irregular forms | three verb forms with a translation |
| Study cards | a term, a text, or a text with a blurred part to reveal |
| Test cards | single choice, multiple choice, short answer, cloze, matching |

**Runs practice sessions.** A session is assembled from three collapsed blocks: *what to study*
(item by item), *which exercises* (a checkbox list with presets), and a *quick filter* by priority
— random, recently studied, longest untouched, hardest, never seen, or due for review — plus a
count, whether learned items are eligible, and shuffling.

The session itself has two phases: browsing the cards (freely back and forth, with a card list and
jump-to), then a queue of tests where a correct answer removes the card and a wrong one puts it
back a few positions later.

**Checks typed answers tolerantly, but in one direction only.** A leading article or `to` and any
`[bracketed]` part may be omitted, `/` separates equivalent variants, and `sb`/`somebody`,
`sth`/`something` are interchangeable — but typing something the entry never contained is still
wrong. When the leading `a`/`an`/`the`/`to` belongs to the expression itself, braces keep it
required: `{a} few` — which is not the word *few* — or `{to} and fro`. The full notation is in
[EasyPeasy.Docs/Guides/entry-notation.md](EasyPeasy.Docs/Guides/entry-notation.md).

**Speaks and listens.** Text-to-speech with a per-language voice picker, playback of the recorded
pronunciation, and — where the platform supports it — checking the learner's own pronunciation
through speech recognition. Recognition is unavailable on Android, so those exercises are not
offered there at all.

**Tracks progress.** Every item carries a difficulty rating, a review count and a last-review date.
The rating moves automatically with the results (typing a word and saying it out loud weigh more
than picking from four options) and can also be set by hand. The home page shows the day streak,
summary statistics and recent activity.

**Moves content in and out.** A whole course exports to a ZIP archive and imports back; the
*Update* mode matches existing items by a stable `RecordGuid`, so re-importing an edited archive
updates what is there instead of duplicating it.

## Not only English

The name is historical — nothing in the data model is tied to English.

A course carries its own language, chosen from a curated BCP-47 list (English US/GB, Ukrainian,
German, French, Spanish, Italian, Polish, Portuguese, Turkish) and shown with its flag, so courses
in different languages live side by side in one database. The learning machinery — cards,
exercises, answer matching, ratings, filters, streaks — never looks at *which* language a word is
in; it works on a "term ↔ translation" pair.

The subject level above courses is free-form, and study/test cards have no linguistic nature at
all, so the same app serves terminology, definitions or any other material that fits that pair.

The one real limit is voice: synthesis and pronunciation checking are wired for British English,
American English and Ukrainian (`SpeechLanguage`, `NativeVoiceCodes`). For other languages a
*recorded* pronunciation still plays back normally, and every other exercise works unchanged.

## Getting started

```bash
dotnet restore EasyPeasy.sln
```

Run the app on Windows:

```bash
dotnet build EasyPeasy.App/EasyPeasy.App.csproj -f net9.0-windows10.0.19041.0
```

Run every test project (570 tests across 9 of them):

```bash
dotnet test EasyPeasy.sln
```

Full prerequisites and troubleshooting: [EasyPeasy.Docs/Guides/getting-started.md](EasyPeasy.Docs/Guides/getting-started.md).

The course archives the app was built for are not in this repository — they are large, and the
material in them is not mine to publish. So a fresh clone starts with an empty library, and the
**Курси** page offers three ready-made courses instead: English for an interview, irregular verbs,
and phrasal verbs. Between them they use every kind of study and test card, and the phrasal-verb
module doubles as a worked example of the
[entry notation](EasyPeasy.Docs/Guides/entry-notation.md). Adding one saves it exactly as an
imported archive would, so everything in it can be edited or deleted afterwards.

## Licence

Copyright (c) 2026 Tetiana Almukhametova. All rights reserved.

The code is public so it can be read: clone it, build it, run it, judge it. Anything beyond that —
using it, changing it, redistributing it — needs permission. See [LICENSE](LICENSE).

## Solution map

| Project | Role |
|---|---|
| `EasyPeasy.Core` | domain model: entities, DTOs, repository/service interfaces, enums, mapping |
| `EasyPeasy.Data` | EF Core / SQLite: `EasyPeasyDbContext`, repositories, migrations, learning queries |
| `EasyPeasy.Business` | domain services, depending on Core's interfaces rather than on Data |
| `EasyPeasy.Cache` | small in-memory working set (current unit, session items) |
| `EasyPeasy.App` | the MAUI Blazor Hybrid app and the composition root |
| `EasyPeasy.ContentTools` | console tool for authoring the course ZIP archives |
| `EasyPeasy.Docs` | architecture diagrams, developer guides, decision log |
| `MukhaLab.*` | reusable infrastructure: database base types, loggers, query parameters |

![Solution architecture](EasyPeasy.Docs/Diagrams/solution-architecture.png)

Business depends only on interfaces declared in Core, and App is the composition root that wires
Data's concrete repositories to them. The other diagrams — the exact project-reference graph, the
domain entities and the database schema — are in
[EasyPeasy.Docs](EasyPeasy.Docs/README.md#diagrams), which is also where the developer
documentation starts.

## Renaming

The solution was renamed from **EasyEnglish** to **EasyPeasy**, and the two names that are identity
rather than labels were carried over rather than simply rewritten:

- **`ApplicationId`** is now `ua.mukhalab.easypeasy`. On Android and Windows this is the package
  identity, so an installed build under the old id stays on the device as a separate app with its
  own data — export the courses as ZIP from it before switching, or reinstall and re-import.
- **The database file** is now `mukhalab.easypeasy.db`. The app renames an existing
  `EasyEnglish.db` on first start (`MauiProgram.MoveDatabaseFromLegacyName`), together with its
  `-wal` and `-shm` companions, so nothing has to be exported: a pending write-ahead log travels
  with the database instead of being left behind. The move only runs when the new file is absent
  and the old one is there, and a failure is logged rather than thrown — worst case the app opens
  an empty database instead of refusing to start.

The repository folder itself is still named `EasyEnglish`; renaming it is a local operation, done
with the IDE closed.
