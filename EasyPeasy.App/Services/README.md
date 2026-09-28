# EasyPeasy.App/Services

App-local services for `EasyPeasy.App` (the MAUI Blazor Hybrid host) — everything under
`Services/`, `Services/Speech/`, and `Services/SpeechRecognition/`. Unlike `EasyPeasy.Business`
(the shared domain-service layer, referenced by DI as `IWordService`/`IUnitService`/etc.), these
services are either tied to MAUI platform APIs (`Preferences`, `FileSystem`, `FilePicker`, `Share`,
`ITextToSpeech`, `Plugin.Maui.Audio`) or are app-specific presentation/session logic that has no
reason to live in a reusable library. This document does not cover `EasyPeasy.App`'s Razor
components — only the `Services` folder.

Two files live in the bare `namespace EasyPeasy.Services;` (no `.App` segment) rather than
`EasyPeasy.App.Services`: `FileService.cs` and `CourseZipBackupService.cs`. This is a pre-existing
naming quirk, not a typo — don't confuse it with `EasyPeasy.Business.Services`, the shared
library's (differently-named) service namespace.

## Project layout

### `Services/`

| File | Purpose |
|---|---|
| `AudioService.cs` | Plays a single in-memory audio clip (pre-recorded pronunciation bytes) via `Plugin.Maui.Audio`. Distinct from the `Speech/` text-to-speech subsystem. |
| `CourseZipBackupService.cs` | Exports/imports a whole course as a ZIP archive (`course.json` manifest + per-unit JSON + audio files). `namespace EasyPeasy.Services;`. |
| `ExampleMarkdownService.cs` | Renders the app's small markdown subset (bold/italic/bold-italic/code/links/newlines) to HTML, plus "hidden text" span parsing/rendering for fill-in-the-blank example sentences. |
| `FileService.cs` | Cross-platform save/pick-file operations (`FilePicker`, `Share`, platform-specific save paths for Android/iOS/macOS). `namespace EasyPeasy.Services;`. |
| `HomeStatsService.cs` | Home-page stats (today's review count, library totals) computed live from `EasyPeasy.Business` services — no separate progress bookkeeping. |
| `ImageDataUriHelper.cs` | Detects an image format from its magic-number header and builds a `data:` URI for inline `<img>` rendering. |
| `LanguageFlagHelper.cs` | Maps a BCP-47 language code to the wwwroot-relative path of a self-hosted SVG flag icon. |
| `LocalStorageService.cs` | The real `IStorageService` implementation — MAUI `Preferences` + JSON. Backs `EasyPeasy.Cache`'s cache services and everything below that takes `IStorageService`. |
| `PracticeQueueService.cs` | In-memory ordered queue of practice segments (words/examples/irregular forms/study cards/test cards) for one course-practice run. |
| `RecentActivityService.cs` | Persists the last unit the learner opened, for the home page's "continue" shortcut. |
| `StorageMaintenanceService.cs` | Removes storage keys left behind by retired features; run once at startup, idempotent. |
| `LegacyDatabaseMover.cs` | Renames a database left from before the app was EasyPeasy (`EasyEnglish.db` → `mukhalab.easypeasy.db`) together with its `-wal`/`-shm` files, before the `DbContext` is registered. Runs only when the new file is missing and the old one is there, and every step checks its own destination, so an interrupted start finishes on the next one. |
| `StreakService.cs` | Tracks the daily-visit streak (consecutive calendar days visited). |
| `TextBracketsRemoverService.cs` | Strips `[bracketed annotations]` from example/definition text. |
| `AnswerMatcher.cs` | Decides whether a typed answer matches the expected word/phrase: optional articles, a leading infinitive `to`, any number of `[optional]` groups, sb/sth placeholder spellings, and `/` for equivalent wordings (spaced — whole alternatives; glued — a choice for one position). Omitting optional parts is allowed; adding words the entry does not have is not. `{literal}` switches that leniency off inside the braces — the way a leading `a`/`an`/`the`/`to` that belongs to the entry is kept required (`{a} few`, which is not the word *few*). Full notation: [entry-notation.md](../../EasyPeasy.Docs/Guides/entry-notation.md). |
| `WordRatingCalculator.cs` | Spaced-repetition rating: `UpdateWordAfterSession` applies a post-session rate change; `CalculateCurrentRate` estimates a read-only "decayed" rate via a forgetting-curve model. Also defines `CardType`/`CardDirection`. |

### `Services/Speech/` — text-to-speech

| File | Purpose |
|---|---|
| `SpeechLanguage.cs` | The 3 voiceable languages: `EnglishBritish`, `EnglishAmerican`, `Ukrainian`. |
| `LocaleInfo.cs` | Normalized voice/locale record, built from either MAUI's native locale list or the Windows Web Speech API. |
| `SpeechSegment.cs` | One chunk of text to speak, with a primary language and an inclusion language for `**marked**` sub-spans. |
| `TextChunkParser.cs` (`internal`) | Splits a segment's text at `**markers**` into language-tagged chunks. |
| `NativeVoiceCodes.cs` (`internal`) | The `SpeechLanguage` → native `lang-COUNTRY` code table plus voice-matching logic, shared by `MauiSpeechService`/`VoiceAvailabilityService`/`VoicePickerViewModel` (previously duplicated 3x — see Known Issues #3). |
| `SpeechPlayer.cs` | Scoped Blazor-facing wrapper: cancels/replaces in-flight playback on every `Play*Async` call. |
| `VoiceSettings.cs` | The learner's chosen voice ID per language, as persisted data. |
| `VoiceSettingsService.cs` | Persists/loads `VoiceSettings` via `IStorageService` (one storage key per language). |
| `ISpeechEngine`/`MauiSpeechEngine.cs` | Low-level "speak this text with this exact voice" — MAUI `ITextToSpeech`-backed implementation. |
| `IVoiceProvider`/`MauiVoiceProvider.cs` | Lists every voice available on-device, cached until invalidated. |
| `ISpeechService`/`MauiSpeechService.cs` | Default speech orchestration: parses segments into chunks, resolves a voice per chunk (saved choice → best native match → any match), plays them in order. |
| `VoiceAvailabilityService.cs` | Reports per-language whether a usable voice exists, with install instructions when it doesn't. |
| `VoicePickerViewModel.cs` | Backs the voice-selection settings screen (`ObservableObject`, grouped voice lists, preview playback). |

*(`ISpeechEngine`, `IVoiceProvider`, `ISpeechService` are declared in `EasyPeasy.App/Interfaces/`, not this folder, but are documented alongside their implementations since they're this subsystem's contracts.)*

### `Services/SpeechRecognition/` — pronunciation checking

| File | Purpose |
|---|---|
| `PronunciationCheckResult.cs` | Result record + `PronunciationConfidence` enum for one pronunciation check. |
| `PronunciationTextNormalizer.cs` | Strips bracketed annotations and a leading "to"/article before comparing against spoken input. |
| `UnsupportedPronunciationCheckService.cs` | Fallback `IPronunciationCheckService` for platforms without a wired-up recognizer (currently: everything but Windows) — `IsSupported` is `false`; `CheckAsync` always throws. |

`IPronunciationCheckService` (in `EasyPeasy.App/Interfaces/`) is likewise documented alongside its implementation for the same reason.

## Known Issues & Suggested Improvements

Found while documenting this layer. Items #1–5 were fixed in a later pass (kept here, struck
through, as a record of what changed and why); #6 is still open.

1. ~~**`WordRatingCalculator.UpdateWordRate<T>` swallows every exception and returns an empty
   list** instead of the partial results or the exception.~~ **Fixed.** The outer `try`/`catch` is
   gone — any exception from processing an item now propagates. This was safe to do only after
   fixing #2 below (see next item); before that fix, this method would have thrown on *every* call.

2. ~~**`WordRatingCalculator.UpdateWordAfterSession`'s inner `try`/`catch` blocks swallow *any*
   exception**, not just a missing-key lookup.~~ **Fixed — but this one wasn't just defensive
   code, it was load-bearing.** `TestDetailModel`'s `CardType` indexer only handles 6 of the 8
   `CardType` values — `Review` and `QuickAnswer` are synthetic, sort-priority-only values (see
   their doc comments) that the indexer doesn't support and throws `ArgumentOutOfRangeException`
   for. Since the loop iterated *every* `CardType` via `Enum.GetValues<CardType>()`, it hit that
   exception on every single call and relied on the `catch` to skip it — removing the `catch`
   without also fixing the root cause would have broken the feature entirely. The actual fix:
   iterate a new `TrackableCardTypes` list (the 6 real types) instead of every enum value, so
   `Review`/`QuickAnswer` are skipped by construction and any *other* exception is a genuine bug
   that now surfaces. The 5 Razor call sites (`WordsMultiTest.razor`,
   `IrregularFormsMultiTest.razor`, `StudyCardsMultiTest.razor`, `TestCardsMultiTest.razor`,
   `ExamplesMultiTest.razor`) each wrap their `FinishTest()` call in a `try`/`catch` that sets the
   existing `_errorMessage` field, so a genuine failure now shows an error instead of crashing the
   page or silently discarding progress.

3. ~~**The `NativeCodes` dictionary (`SpeechLanguage` → native locale codes) and its matching
   logic are duplicated identically in three places**~~ **Fixed.** Extracted to a new internal
   `NativeVoiceCodes` static class (`Services/Speech/NativeVoiceCodes.cs`) with `For()`,
   `BareLanguageCode()`, and `Matches()`; `MauiSpeechService`, `VoiceAvailabilityService`, and
   `VoicePickerViewModel` all use it now instead of their own copies.

4. ~~**`WordLearningService.GetPageRoute`/`ParsePageRoute` only cover `SingleChoice`, `KnowOrNot`,
   `ManualInput`, and the synthetic `Review` priority**~~ **Removed the whole class.** Investigating
   the gap found it had **zero real callers anywhere in the app** — the only trace was a
   commented-out `//services.AddScoped<WordLearningService>();` in `MauiProgram.cs` — and that the
   job it was built for is already done, better, by the `TestDefinition<T>`/`TestRegistry` system
   (`Components/Pages/Drilling/Definitions/`) that the drilling UI actually runs on: each
   `TestDefinition<T>` subclass (e.g. `WordToTranslationSingleChoiceDef`) carries its own
   `HeaderClass`/`IconClass`/`ComponentType` directly as properties, and ordering comes from
   `TestRegistry`'s declared list order — no central switch statement to fall out of sync with new
   card types. `WordLearningService.cs` and its test file were deleted; the now-fully-orphaned
   `CardType.Review`/`CardType.QuickAnswer` enum values (which existed only to feed
   `GetTestPriority`) were removed too.

5. ~~**`MauiSpeechEngine._cts` is constructed once and never canceled, disposed, or
   replaced**~~ **Fixed.** Removed — the field served no purpose since it was never canceled; the
   caller-supplied `CancellationToken` (already threaded through from `SpeechPlayer`/
   `MauiSpeechService`'s own cancellation) is passed straight through to `_tts.SpeakAsync` now.

6. **`WordRatingCalculator.CalculateCurrentRate<T>` has no callers anywhere in the solution** —
   confirmed via a full-solution search. It's public API, so may be intentionally exposed for a
   "sort by urgency" feature that hasn't been built yet rather than dead code; worth confirming
   before ever deleting it.

## Testing

`EasyPeasy.App.Tests` (103 tests) covers the pure-logic pieces with no MAUI dependency —
`WordRatingCalculator`, `ExampleMarkdownService`, `TextBracketsRemoverService`,
`PronunciationTextNormalizer`, `TextChunkParser`, `LanguageFlagHelper`,
`ImageDataUriHelper`. Everything else in this layer is either thin MAUI-platform glue
(`AudioService`, `FileService`, `LocalStorageService`) that would need platform-API fakes to test
meaningfully, or Blazor-facing orchestration (`SpeechPlayer`, `VoicePickerViewModel`) that's more
naturally covered by component/integration tests than unit tests — neither is covered here.

**Why a separate project instead of referencing `EasyPeasy.App.csproj` directly**: `EasyPeasy.App`
is a MAUI head project (`UseMaui=true`, `MauiIcon`/`MauiSplashScreen`/`MauiImage` resizetizer items,
self-contained Windows packaging). Referencing it as a `ProjectReference` from a plain test project
isn't supported by the MAUI SDK tooling — confirmed by two independent failures when this was tried:
the Windows target's `StaticWebAssets` compression step and the Resizetizer's duplicate-icon check
both error out when the head project is pulled in as a library reference rather than built
standalone. Instead, `EasyPeasy.App.Tests` targets plain `net9.0` (no MAUI) and pulls in the
pure-logic files directly via `<Compile Include>` with a `Link` path — the real production source,
not a copy, so edits to those files are automatically reflected in the tests — plus a
`ProjectReference` to `EasyPeasy.Core.csproj` for the model types (`WordModel` etc.) that
`WordTestModel`/`ITestSessionItem` build on. This sidesteps the MAUI toolchain entirely rather than
fighting it.

Priority, per the same risk-based framework used elsewhere in this solution: `WordRatingCalculator`
got the most coverage since it's the highest-complexity, highest-consequence piece — a wrong rating
silently mis-schedules review for every word in the app.

### What's covered, per file

- **`WordRatingCalculatorTests.cs`** (26 tests) — `GetAvailableDirections` for every mapped
  `CardType` plus the unmapped-returns-empty case; `CalculateCurrentRate`: never-reviewed and
  reviewed-today are no-ops, an unreviewed-for-days item's rate increases, the exact result is
  checked against the documented forgetting-curve formula (`maxPenalty * (1 - e^(...))`) as a
  characterization test, a higher review count + success rate decays slower than a low one, and the
  result clamps at `MAX_RATE`; `UpdateWordAfterSession`: null `Tests` and zero-attempts are no-ops, a
  perfect `ManualInput`/`TranslationToWord` result decreases the rate by the exact weighted amount
  (`-0.585 * 1.5 * 1.05` — base change × card-type impact × attempt modifier), typed and spoken
  answers move the rate further than single choice and further still than self-assessment, a mixed
  session stays between the single-type extremes, an all-wrong session increases it, attempts across multiple direction×type
  combinations sum correctly while `ReviewCount` still only increments once, and the rate clamps at
  both `MIN_RATE`/`MAX_RATE`; a baseline test confirms a real `CardType` still processes correctly
  through the `TrackableCardTypes` allowlist (**Known Issue #2**, now fixed — the allowlist used to
  exclude two synthetic `CardType` values, `Review`/`QuickAnswer`, which have since been removed
  entirely along with their sole consumer, `WordLearningService`); `UpdateWordRate` (batch): skips
  items with null
  `Tests`, still includes unchanged zero-attempt items, handles an empty list, and
  `UpdateWordRate<WordTestModel>(null!)` now throws instead of silently returning an empty list
  (**Known Issue #1**, now fixed).
- **`ExampleMarkdownServiceTests.cs`** (26 tests) — `RenderMarkdown` for every marker
  (bold-italic/bold/italic/code/link/newline) plus null/whitespace input and the
  hidden-marker-is-skipped behavior; `ParseHiddenText` (first match, no match, `marker=None`);
  `HasHiddenText`; `RenderMarkdownWithHidden` (revealed vs. blurred CSS class);
  `StripMarkdown`; `GetHiddenTextOnly` (match and no-match); `CheckAnswer` (case/whitespace
  insensitivity); `ParseSegments` (no markers, multiple occurrences, marker at the very start, empty
  string).
- **`TextBracketsRemoverServiceTests.cs`** (11 tests) — bracket removal across the documented
  spacing rules: no brackets, trailing-space collapse, no-trailing-space-but-next-is-punctuation,
  no-trailing-space-but-next-is-a-letter (joins words), adjacent brackets, bracket-only input, `null`
  input, and multiple annotations in one string.
- **`PronunciationTextNormalizerTests.cs`** (12 tests) — each leading prefix (`to`/`an`/`a`/`the`,
  case-insensitively) is stripped, a word with no prefix is untouched, only one prefix is ever
  stripped (not chained), a prefix-*like* word (e.g. "another") is left alone, a standalone `"to "`
  isn't reduced to an empty string, and bracket removal composes correctly before prefix-stripping.
- **`TextChunkParserTests.cs`** (8 tests) — no markers, marker in the middle/at the start/at the end,
  multiple markers alternating languages, marker content with padding whitespace gets trimmed, and
  empty/whitespace-only input returns an empty list. (One originally-planned case — `"****"`
  producing an empty-content match — turned out to rest on a wrong assumption about the regex;
  `.+?` requires at least one character, so four asterisks never actually matches as empty content.
  Caught by the test itself failing and replaced with a real edge case instead.)
- **`LanguageFlagHelperTests.cs`** (12 tests) — valid codes with `-`/`_` separators and mixed case,
  `null`/empty/whitespace/malformed codes falling back to the neutral flag, and a characterization
  test for a real quirk: a bare 2-letter code with no separator (e.g. `"en"`) is treated as if it
  *were* the region, since `Split(...).LastOrDefault()` returns the original string unchanged when
  there's nothing to split on.
- **`ImageDataUriHelperTests.cs`** (10 tests) — magic-number detection for PNG/GIF/WEBP/JPEG,
  the too-short-for-any-signature and unrecognized-bytes fallbacks to JPEG, `BuildDataUri` returning
  `null` for `null`/empty input, and a built URI's MIME type + base64 payload matching exactly.

Not covered: `CalculateForgettingPenalty`/`CalculateMemoryStrength` are private and only exercised
indirectly through `CalculateCurrentRate`, consistent with this solution's practice of testing
through the public API rather than reflecting into private methods.
