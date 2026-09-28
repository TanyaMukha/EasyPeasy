# Testing Strategy

A risk-based framework for deciding *what* tests a project needs — not a line-coverage target.
Applied consistently across every `.Tests` project in this solution; use it when adding tests to a
new or currently-untested project too.

## Core principle

Tests cover **risk**, not lines. A silently wrong result (no exception, just the wrong answer) is
worse than a loud crash — prioritize catching the former. Every bug found while building out this
solution's test suites was exactly this shape: wrong data returned with no error, not a crash.

## Per public method, ask 4 questions

1. **Happy path** — does it do the obviously-right thing for normal input?
2. **Boundary values** — `null`, empty, `0`, `-1`, max, one-item vs. many-item collections.
3. **Error paths** — does it throw, and which exception type? (See
   [conventions.md](conventions.md)'s exception-handling section for the expected types.)
4. **Every branch** (`if`/`switch`) as its own case. Use equivalence partitioning +
   `[Theory]`/`[InlineData]` instead of enumerating every input combination by hand.

## Code smells that predict bugs — prioritize these first

Each of these has caused a real, previously-shipped bug somewhere in this solution:

- **Manual string parsing/splitting of structured input** → a bracket-path parsing bug in
  `MukhaLab.SelectQueryParameters` (`"Children[Author.Name]"`).
- **Implicit culture/locale-dependent conversion** → `Convert.ToDecimal` without
  `CultureInfo.InvariantCulture`.
- **Static/shared mutable state** → an old process-wide log queue in
  `MukhaLab.BrowserConsoleLogger`.
- **Authorization/scoping checks applied inconsistently across methods** → per-user filtering that
  wasn't enforced on every `BaseRepository` method in `MukhaLab.Database`.
- **Concurrent access to the same resource** → delete-during-update needing
  `DbUpdateConcurrencyException` handling, also in `MukhaLab.Database`.
- **A `try`/`catch` around more code than the one call that can actually throw** → swallows
  unrelated failures as if they were the expected case. `WordRatingCalculator.UpdateWordRate`'s
  outer catch (returns an empty list for *any* exception, indistinguishable from "nothing needed
  updating") and its inner per-combination catches (swallow *any* exception reading a test result,
  not just a missing-key lookup) are open, not-yet-fixed examples — see
  `EasyPeasy.App/Services/README.md` Known Issues.

## Prioritization order

1. Public API before internals (but internals still get tested if they're a meaningful risk — see
   `TextChunkParser`, `internal` but covered).
2. Risk × likelihood before cosmetic correctness.
3. Higher branch complexity before simple pass-through code.
4. **Code that already had a bug, before code that never did** ("bug clustering") — every
   previously-untested project in this solution turned up at least one real bug the moment tests
   were written, which is itself evidence for prioritizing untested surfaces generally, not just
   revisiting known-buggy files.

## Regression-test ritual

When a bug is found (in review or in production), write the failing test **first** — it must encode
the exact previously-broken behavior — then fix the code. Don't just add a test that "would have
caught it"; add the test that *did* catch it, asserting the specific wrong output/exception that
used to happen.

Sometimes the answer isn't "fix it yet" — if the correct behavior is a genuine product ambiguity (not
a technical bug with one right answer), write a test that pins down and documents *current* behavior
with an explicit comment, rather than asserting what the doc *says* should happen. That keeps the
test suite honest about what the code does today without silently deciding the product question
for you.

## Process for a new/currently-untested project

1. List every public method.
2. Apply the 4 questions above to each.
3. Flag anything matching the code-smell list.
4. Prioritize per the ordering above.
5. For integration points (DB, JS interop, file I/O, network), test against a real dependency (e.g.
   SQLite in-memory) rather than a mock that can silently diverge from real behavior — this is why
   `MukhaLab.Database.Tests`, `EasyPeasy.Data.Tests`, and `EasyPeasy.Business.Tests`'s
   integration fixtures use a real SQLite connection instead of EF Core's `InMemory` provider.
6. A test double (fake/mock) for a "persistence" interface should simulate the serialization
   boundary — even a cheap JSON round-trip — whenever production code might rely on
   get-after-set returning an independent copy. `EasyPeasy.Cache.Tests`' `FakeStorageService` was
   rewritten for exactly this reason: an early version held raw object references, which let an
   in-memory-cache mutation silently "leak" into the fake's storage through a shared reference — a
   failure mode the real `Preferences`-backed implementation can't exhibit, since it always
   deserializes a fresh object per read.

## What's covered so far

| Project | Tests | Notes |
|---|---|---|
| `EasyPeasy.Core.Tests` | 78 | Pure POCOs mostly untested by design; the 3 pockets of real logic (`TestCardConverters` JSON pack/unpack, `MappingActions`, `RateExtensions`) are covered, plus a container-level guard that every mapping action/converter the profile uses is actually DI-registered. |
| `EasyPeasy.Data.Tests` | 41 | Real in-memory SQLite `DbContext`. |
| `EasyPeasy.Business.Tests` | 29 | Real-SQLite integration tests for `UnitService.ReconcileAndUpdateAsync`, including GUID-only identity and partial-payload merging. |
| `EasyPeasy.Cache.Tests` | 19 | `NSubstitute` for `IWordService`/`IUnitService`; a real `IServiceScopeFactory` (not mocked) to exercise the actual DI-scope-per-fetch mechanism. |
| `EasyPeasy.App.Tests` | 243 | Pure-logic subset of `EasyPeasy.App/Services/` only — see that project's README for why the test project can't `ProjectReference` the MAUI head project. |
| `MukhaLab.SelectQueryParameters.Tests` | 53 | Includes a supplementary real-SQLite check that filter expressions are actually SQL-translatable, not just LINQ-to-Objects-compatible. |
| `MukhaLab.Database.Tests` | 47 | Real SQLite in-memory connection. |
| `MukhaLab.BrowserConsoleLogger.Tests` | 32 | `NSubstitute` for `IJSRuntime`; `[InternalsVisibleTo]` exposes an internal accessor so async flush behavior can be awaited deterministically instead of polled. |
| `MukhaLab.LoggerExtensionDelegate.Tests` | 28 | `Microsoft.Extensions.Diagnostics.Testing`'s `FakeLogger`/`FakeLogCollector`. |

**570 tests total**, all green as of the last full solution run.

## What's not covered, and why that's a deliberate choice, not an oversight

- `EasyPeasy.App`'s Razor components/pages, and the MAUI-platform-glue services in
  `EasyPeasy.App/Services/` (`AudioService`, `FileService`, `LocalStorageService`,
  `SpeechPlayer`, `VoicePickerViewModel`, etc.) — these need platform-API fakes or a
  component/integration-test approach (e.g. bUnit), not a unit-test pattern copy-pasted from the
  rest of this solution.
- `EasyPeasy.ContentTools` — a standalone content-authoring CLI, not part of the running app.
- `MukhaLab.LoggerExtensionDelegate` is tested but not currently *used* anywhere in the solution —
  low urgency to extend its coverage further until something actually depends on it.
