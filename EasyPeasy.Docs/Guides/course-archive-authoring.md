# Course Archive Authoring

How to build a course ZIP by hand — the format the app's "Export course" produces and its "Import
course ZIP" reads back. Audience is whoever is authoring content outside the app, usually through
[`EasyPeasy.ContentTools`](../../EasyPeasy.ContentTools/README.md).

For how an individual word or phrase is written inside a unit, see
[entry-notation.md](entry-notation.md) — this guide covers the container, that one covers the
contents.

## When to build one by hand at all

Adding a handful of words is faster in the app. Build the archive programmatically when the volume
makes hand entry impractical — a full grammar drill of a hundred cloze cards, a vocabulary table
pasted from elsewhere, or the same correction applied across every card in a unit.

The app remains the authority on the format: `CourseZipEditor.JsonOpts` is kept byte-for-byte in
sync with `CourseZipBackupService.JsonOpts`, because mismatched serializer settings produce a file
the importer cannot read back.

## What is inside an archive

| Path | Required | Holds |
|---|---|---|
| `course.json` | yes | The manifest — course identity, options, and the list of unit files |
| `units/unit_N.json` | yes | One serialized `UnitModel` per module: words, cards, irregular forms |
| `audio/*.mp3` | no | Pronunciation recordings, referenced per unit from the manifest |
| `content/<unitGuid>.html` | no | A unit's content page, referenced as `contentFile` in the manifest |

`unit_N.json` numbering is a naming convention, nothing more. Nothing reads meaning into `N`; the
manifest is what maps a file to a unit.

## The manifest is the only index

**A unit file that is not listed in `course.json` does not exist as far as the importer is
concerned.** Adding `units/unit_12.json` to the ZIP and stopping there produces an archive that
imports cleanly and silently drops the new module.

Each entry looks like this:

```json
{
  "unitGuid":   "ffdba34b-8ab5-44b3-9f44-e4f009552078",
  "fileName":   "units/unit_12.json",
  "title":      "Vocabulary #8",
  "wordCount":  30,
  "audioFiles": [],
  "imageFiles": []
}
```

`unitGuid` must equal the unit's own `recordGuid`. `audioFiles` lists the archive-relative paths of
that unit's recordings; leave it empty when audio is recorded later inside the app.

`CoursePackageManifest` — the manifest's typed model — lives in `EasyPeasy.App`, which the console
tool does not reference. `CourseZipEditor` therefore patches `course.json` as a raw `JsonNode` tree
instead, touching only the `units` array.

## Identity: `RecordGuid` versus `Id`

Two different identifiers, and mixing them up is the most consequential mistake available here.

| Field | Set it to | Why |
|---|---|---|
| `recordGuid` | a fresh `Guid` per new record | Stable identity across devices and re-imports |
| `id`, `unitId`, `wordId`, `courseId` | **`0`** | Local database keys; EF assigns real ones on cascaded insert |
| `updatedAt` | `null` on new records | Anything else claims an edit history that does not exist |

`RecordGuid` is what reconciliation matches on when the same course is imported again: a child with
a known GUID is updated in place, an unknown one is inserted. Carrying a non-zero `Id` from some
earlier export into a hand-built archive points at a row that may not exist on the target device.

## Writing the entries

Every word or phrase goes in using the notation from [entry-notation.md](entry-notation.md). The
markers are authoring syntax — `AnswerMatcher` parses them and `EntryTextRenderer` strips them
before the learner sees anything — so anything outside that syntax is treated as literal text the
learner has to type.

The failures worth naming, because each one produces an entry that looks fine and fails silently at
practice time:

| Written | What happens | Correct |
|---|---|---|
| `to roll back (to a previous version)` | Round brackets are not a marker — they become part of the required answer | `to roll back [to a previous version]` |
| `to send somebody back for review` | Literal `somebody`, not dimmed, not interchangeable with `sb`/`someone` | `to send sb back for review` |
| `still (= anyway)` | The gloss lands inside the string the learner must type | `still` — glosses belong in `Note` |
| `to restore from a backup` | Works, but rejects the natural *restore **the data** from a backup* | `to restore [sth] from a backup` |

Braces are only for a leading `a`/`an`/`the`/`to` that is inseparable from the expression —
`{to} date`, `{a} priori`. An ordinary infinitive keeps its `to` bare so that both typings pass.

## The authoring run

Each authoring session is its own class in `EasyPeasy.ContentTools/Program.cs`, dispatched by
module key. The shape is always the same:

```csharp
CourseZipEditor.CopyArchive(SourceZip, TargetZip);   // never edit in place
var unit = CourseZipEditor.LoadUnit(TargetZip, UnitFile);
// ...append words / StudyCard / TestCard entries via CardBuilders...
CourseZipEditor.SaveUnit(TargetZip, UnitFile, unit);
```

Copying first matters: a failed run leaves the source archive intact, and the chain of
`_updated`, `_updated2` files is a usable history of what was done to what.

For a brand-new unit rather than an append, build a fresh `UnitModel`, save it, **then** patch the
manifest:

```csharp
var manifest   = CourseZipEditor.LoadManifestNode(TargetZip);
var unitsArray = manifest["units"]!.AsArray();
unitsArray.Add(new JsonObject { /* unitGuid, fileName, title, wordCount, audioFiles, imageFiles */ });
CourseZipEditor.SaveManifestNode(TargetZip, manifest);
```

Old module classes stay in the file after their run. They are a record of which archive was
produced from which, not dead code to tidy away.

## Before importing

```bash
dotnet run -- verify "<path-to.zip>" units/unit_N.json
```

`verify` is the only read-only operation in the tool. It prints the unit title and the count of
words, irregular forms, and cards broken down by kind — enough to catch an empty unit, a unit saved
without its cards, or a card kind that did not get built.

Then check, in order:

1. The new unit appears in `course.json`'s `units` array, with `unitGuid` matching its
   `recordGuid`.
2. `verify` reports the counts you expect.
3. Every `id` / `unitId` / `wordId` in the new content is `0`.
4. Entries use `[]` / `{}` / `/` / `sb` / `sth` — and no round brackets outside literal text.
5. Every path in `audioFiles` and `contentFile` actually exists in the archive.

Import onto a device with something to lose only after that. Reconciliation matches on
`RecordGuid`, so a re-import of the same archive updates rather than duplicates — but a wrong GUID
inserts a second copy, and that is tedious to unpick by hand.
