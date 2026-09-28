# Key Architectural Decisions

Lightweight ADR-style log of the decisions most likely to confuse a future contributor if left
unexplained — either because they're not obvious from reading the code alone, or because someone
could plausibly "fix" them back to the wrong state without knowing why they're the way they are.
Newest first isn't required; grouped by topic instead.

Each entry: **Context** (the problem), **Decision** (what was chosen), **Consequences** (what that
buys you / what it costs).

---

## 1. Business depends on Core's interfaces only, never on Data directly

**Context**: `EasyPeasy.Business` (the domain service layer — `WordService`, `UnitService`, etc.)
originally had a `ProjectReference` to `EasyPeasy.Data`, but nothing in its source actually used
any `EasyPeasy.Data` type — the reference was dead weight.

**Decision**: Removed the reference. `EasyPeasy.Business` depends only on repository/service
interfaces declared in `EasyPeasy.Core` (`IWordRepository`, `IUnitRepository`, etc.).
`EasyPeasy.Data` implements those interfaces concretely. `EasyPeasy.App`'s `MauiProgram.cs` is
the only place that references both projects, and it's where the concrete repositories get wired to
the interfaces Business expects (`AddEasyPeasyRepositories()` then `AddEasyPeasyDataServices()`).

**Consequences**: Classic Dependency Inversion — `EasyPeasy.Business` could be tested or reused
against an entirely different persistence layer without changing a line of business logic. The cost
is one extra hop to trace when reading code: a `WordService` method that "obviously" talks to a
database doesn't reference anything database-shaped directly, only an interface. See
[project-dependencies.puml](../Diagrams/project-dependencies.puml) for the exact graph this
produces.

---

## 2. Project names are plain English, not layer acronyms

**Context**: Considered renaming `EasyPeasy.Business` to something shorter/more classic like
`BLL` (business logic layer) to match a "DAL/BLL" naming scheme some .NET codebases use.

**Decision**: Kept plain-English names (`Core`, `Data`, `Business`, `Cache`, `App`). Rejected `BLL`.

**Consequences**: `BLL` is a dated convention that would create pressure to also rename `Data` to
`DAL` for consistency, and plain names are self-explanatory to anyone regardless of which .NET
era they learned conventions in. `Business` on its own is a perfectly standard 3-tier layer name —
the thing that actually needed fixing was the namespace mismatch (see #1's sibling issue below),
not the project name.

---

## 3. `MukhaLab.*` is reserved for genuinely reusable code

**Context**: While reviewing `EasyPeasy.App/Services/` for extraction candidates, several files
had "no MAUI dependency" but still encoded EasyPeasy's actual domain (course/unit/word concepts,
English-specific grammar handling) — e.g. `HomeStatsService`, `WordRatingCalculator`,
`PronunciationTextNormalizer`.

**Decision**: "No MAUI dependency" alone doesn't qualify something as reusable. Only code with *zero*
EasyPeasy-domain knowledge belongs under the `MukhaLab.*` prefix (matching the 4 existing
libraries: `SelectQueryParameters`, `Database`, `BrowserConsoleLogger`, `LoggerExtensionDelegate`,
none of which know anything about words, units, or courses).

**Consequences**: This is a naming/extraction *principle*, not yet an executed decision — no new
`MukhaLab.*` library has been created from `EasyPeasy.App` code. If that extraction happens later,
this is the bar a file needs to clear first.

---

## 4. Singleton services needing a scoped dependency use `IServiceScopeFactory`, not the scoped service directly

**Context**: `EasyPeasy.Cache`'s `WordCacheService`/`CurrentUnitCacheService` were registered
`AddSingleton` but took `IWordService`/`IUnitService` (registered `AddScoped` in
`EasyPeasy.Business`) directly in their constructors — a captive-dependency bug: the first-resolved
scoped instance gets frozen for the app's entire lifetime instead of getting a fresh one per logical
operation.

**Decision**: Both cache services now take `IServiceScopeFactory` and create a short-lived
`IServiceScope` per fetch, resolving `IWordService`/`IUnitService` from that scope.

**Consequences**: Correct even if the app starts creating additional DI scopes elsewhere in the
future (a background task, etc.) — today it's harmless in practice since `EasyPeasy.App` never
creates extra scopes, but the bug was latent, not theoretical. Any new singleton that needs a scoped
dependency should follow this same pattern.

---

## 5. `EntityNotFoundException` is the standard "this doesn't exist" exception

**Context**: Not-found handling was inconsistent across the stack —
`WordService.UpdateWordRateAsync` used to throw `NullReferenceException` via a lying `!`
null-forgiving operator over an actually-null value; `UnitService.ReconcileAndUpdateAsync` threw
`ArgumentException`.

**Decision**: Every not-found path throws `MukhaLab.Database`'s `EntityNotFoundException`, including
the case where a row is deleted out from under an in-flight update (caught as
`DbUpdateConcurrencyException` and rewrapped).

**Consequences**: A caller only needs to catch one exception type for "this thing doesn't exist,
whether it never did or was deleted since you loaded it." Any new not-found path should match this.

---

## 6. `null` on a child collection means "don't touch it," never "delete everything"

**Context**: `UnitService.ReconcileAndUpdateAsync` (GUID-based reconcile of a unit's word/example/
irregular-form/study-card/test-card collections) treated a `null` incoming collection as "delete
every existing item of that kind" when `deleteMissing: true`. The one real call site
(`ImportCourseZip.razor`) passes units deserialized from a backup-ZIP's JSON, which can plausibly
omit a child array — confirmed by defensive `?.Count ?? 0` code already present elsewhere in
`CourseZipBackupService`. This made a partial/older backup silently wipe data on import.

**Decision**: A `null` incoming child collection is now filled from the *existing* entity's
collection before diffing — so `null` behaves as "no opinion, leave it alone," and only an
explicit (possibly empty) list can add/remove/delete items.

**Consequences**: Importing an older or partial backup ZIP can no longer silently delete data it
simply didn't know about. If you write a new reconcile-style method anywhere, treat `null` the
same way.

---

## 7. `LearningPriority.Old` spans both reviewed and never-reviewed items

**Context**: `LearningPriority.Old`'s filter was byte-for-byte identical to `LearningPriority.New`'s
(`LastReviewDate == null`), which didn't match its own XML doc or the UI labels ("Найстаріші"/"Давно
не повторювались"). Genuinely ambiguous — not a bug with one obvious right answer, since New/Old
being a matched pair over the same never-reviewed pool was also a defensible reading.

**Decision**: After being raised twice (once answered "leave as-is," reversed in a later
conversation with no new evidence), `Old` was changed to rank by
`COALESCE(LastReviewDate, CreatedAt)` ascending — spanning *every* item, ordered by whichever
timestamp reflects "last touched." A long-neglected never-reviewed item and a long-ago-reviewed item
now both surface as "old."

**Consequences**: `New` and `Old` are no longer a matched pair over the same pool — `New` still only
covers never-reviewed items; `Old` now covers everything. If this reads as surprising later, that's
the history: it was a judgment call on ambiguous product intent, not a mechanical bug fix.

---

## 8. `EasyPeasy.App.Tests` compiles source files directly instead of `ProjectReference`-ing the MAUI app

**Context**: `EasyPeasy.App` is a MAUI head project (`UseMaui=true`, `OutputType=Exe`,
`MauiIcon`/`MauiSplashScreen`/`MauiImage` resizetizer items, self-contained Windows packaging).
Referencing it as a `ProjectReference` from a test project — the pattern every other `.Tests`
project in this solution uses — failed in two independent, unrelated ways: a `StaticWebAssets`
compression step (`InvalidOperationException: File length ... is not defined`) and, once past that,
the Resizetizer's duplicate-icon detection (`Microsoft.Maui.Resizetizer.After.targets`: "duplicate
file names detected... appicon"). Neither is a cache/config issue — referencing an app-type MAUI
project from another project isn't supported by this tooling.

**Decision**: `EasyPeasy.App.Tests` targets plain `net9.0` (no MAUI) with **no** `ProjectReference`
to `EasyPeasy.App`. Instead it pulls in the ~10 pure-logic `.cs` files it needs directly via
`<Compile Include="...\EasyPeasy.App\..." Link="..." />` — the real production source, not a
copy — plus a normal `ProjectReference` to `EasyPeasy.Core.csproj` for the model types those files
depend on.

**Consequences**: Sidesteps the MAUI toolchain entirely rather than fighting it; edits to the linked
files are automatically picked up by the tests since it's the same source, not a copy. The
alternative — extracting the pure-logic files into a new class library project — was deliberately
not taken here, since that's a real structural change and a decision (see #3) that hadn't been
opted into. If `EasyPeasy.App/Services/` ever *does* get split into a `MukhaLab.*`-style library,
this test project's `<Compile Include>` list is exactly the set of files that would move.

---

## 9. Real SQLite in-memory over EF Core's `InMemory` provider

**Context**: Needed a way to test EF Core repository/query logic without a real database server.

**Decision**: Every test touching a `DbContext` (`EasyPeasy.Data.Tests`,
`EasyPeasy.Business.Tests`'s integration fixtures, `MukhaLab.Database.Tests`) uses a real
`Microsoft.Data.Sqlite` connection (`Data Source=:memory:`) through EF Core's Sqlite provider, not
the `Microsoft.EntityFrameworkCore.InMemory` package.

**Consequences**: `InMemory` doesn't enforce the same LINQ-to-SQL translation rules as a real
relational provider — it will happily accept query shapes that compile against `InMemory` but throw
or behave differently against real SQLite (which is exactly the class of bug several test suites
exist to catch, e.g. `MukhaLab.SelectQueryParameters`' filter expressions). The cost is slightly
more test-fixture setup (`IAsyncLifetime` to open/close a connection) versus `InMemory`'s
zero-config feel.

---

## 10. PlantUML rendering: Local, not the extension's default Azure demo server

**Context**: PlantUML diagrams in this project (`.puml` files under `EasyPeasy.Docs/Diagrams/`)
silently failed to render in Visual Studio.

**Decision**: The root cause was the "PlantUML Editor" VS extension's default Render Type pointing
at a dead Azure demo server. Fixed via Tools → Options → PlantUML → Advanced → Render Type =
**Local**.

**Consequences**: Anyone opening this repo for the first time and finding diagrams don't render
should check this setting before assuming the `.puml` files themselves are broken.

---

## 11. Course archives carry RecordGuid only — never database IDs

**Context**: Course export/import used an `isFullBackup` flag that, when set, wrote real database
IDs into the archive and restored them verbatim. "Update an existing course" was only offered for
such archives, on the theory that IDs were needed to find the rows to update. Two things were wrong
with that. First, IDs are local to one app instance: importing an archive from another device wrote
foreign IDs into this database. For a child that matched an existing row by GUID this was masked
(the ID got overwritten anyway), but a *new* child kept its foreign ID, and EF then treated it as an
existing row — verified to throw a `DbUpdateConcurrencyException`, and in an ID-collision case it
would have silently overwritten an unrelated word instead. Second, gating "update" on full backups
meant a partial export (a few units, or no examples/progress) could never be used to update, even
though `ReconcileAndUpdateAsync` already matched children by `RecordGuid` and never needed the IDs.

**Decision**: `RecordGuid` is the only identity that crosses the archive boundary. Export always
zeroes IDs; import always resolves real local IDs by GUID. `isFullBackup` is gone from both the
export options and the manifest (schema bumped to `2.0`; `1.0` archives still import — their IDs
are simply ignored). "Update existing course" is now offered whenever the course GUID matches,
regardless of how partial the archive is. `ReconcileAndUpdateAsync` zeroes any unmatched child's ID
itself rather than trusting the caller to have done it.

**Consequences**: Updating from a partial archive is now safe, which required teaching the merge
what the payload is actually authoritative for (`UnitMergeOptions`) — an archive exported without
examples still deserializes each word with an *empty* examples list, and one exported without
progress deserializes with *default* rate/review values. Applied verbatim, both silently destroy
real data; `MergeExamples` and `LearningProgress` exist to distinguish "not included" from "deleted"
and from "reset". See also decision 12.

---

## 12. Learning progress merges newest-wins, per item

**Context**: With updating from partial archives now allowed (decision 11), the same course can
legitimately be studied on two devices and synced in either direction. Blindly applying the
archive's progress means importing a slightly older export silently rolls back everything learned
locally since.

**Decision**: For children matched by GUID, whichever side has the later `LastReviewDate` wins,
carrying `Rate`/`ReviewCount` with it (a rating without the review history that produced it is
meaningless). A never-reviewed item counts as oldest, so a fresh copy of a word can't erase existing
history. When the archive doesn't carry progress at all, the stored values are simply kept
(`LearningProgressMerge.KeepExisting`).

**Consequences**: Progress never moves backwards, in either sync direction, without needing
timestamps beyond what the models already track. The cost is that it's per item rather than per
course — two devices that each studied a *different* subset merge to the union, which is the
desirable outcome here but would surprise anyone expecting whole-course "last write wins".

---

## 13. Finishing the rename: the database file moves itself, the package id does not

**Context**: The solution was renamed from `EasyEnglish` to `EasyPeasy` — projects, namespaces,
assemblies, file names, documents. Two names were held back at first, because they are not labels
but how the platform and the file system find data that already exists: the SQLite file and the
`ApplicationId`. Leaving them was meant to be temporary, and it read as an unfinished rename.

**Decision**: Rename both, and carry the data across where the app is able to.

- **`ApplicationId`** → `ua.mukhalab.easypeasy` (it was still the template's
  `com.companyname.easyenglish.app`). Nothing in the app can migrate this: to the operating system
  a new id is a different application with its own storage. An installed old build simply stays
  installed, with its own data, until it is uninstalled.
- **The database file** → `mukhalab.easypeasy.db`, named after the vendor and the app the way the
  package id is. `MauiProgram.MoveDatabaseFromLegacyName` renames an existing `EasyEnglish.db`
  before the `DbContext` is registered — but only when the new file is absent and the old one is
  present, so it is a one-off, not something that runs on every start.

**Consequences**: The `-wal` and `-shm` files move together with the database. A write-ahead log
can hold committed transactions that have not been checkpointed yet, so moving the `.db` alone
would silently lose the last session's work — the one failure mode that would be invisible until
much later.

The move is wrapped in a `try` that logs and continues. A database that cannot be moved is a bad
day; an app that refuses to start is a worse one, and the old file is still there to recover from
by hand.

Both names being identity also means the two migrations are not symmetrical: the file move happens
inside the same app sandbox and is automatic, while a changed package id leaves the old sandbox
unreachable, and its content has to travel as a course ZIP export.

The repository folder is still `EasyEnglish`; nothing in the build depends on it, since the two
absolute paths Visual Studio had baked into `EasyPeasy.App.csproj` were made relative during the
rename.
