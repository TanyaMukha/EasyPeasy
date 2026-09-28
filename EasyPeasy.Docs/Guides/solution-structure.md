# Solution Structure

One paragraph per project. Each `EasyPeasy.*` library also has its own `README.md` with a full
file-by-file breakdown, a Known Issues section, and (where tests exist) a Testing section — this
page is the map, not a replacement for those. See
[solution-architecture.puml](../Diagrams/solution-architecture.puml) for how these fit together
visually, and [project-dependencies.puml](../Diagrams/project-dependencies.puml) for the exact
`ProjectReference` graph.

## Runtime app layers (bottom to top)

- **[EasyPeasy.Core](../../EasyPeasy.Core/README.md)** — the domain model: entities, DTOs
  ("models"), repository/service interfaces, enums, AutoMapper profiles, rating/difficulty
  extensions. No EF Core, no MAUI — every other `EasyPeasy.*` project depends on this one, and it
  depends on nothing but `MukhaLab.Database` (for `AbstractEntity`/`IGuidRecord`/`IUserContext`).
  78 tests in `EasyPeasy.Core.Tests`.

- **[EasyPeasy.Data](../../EasyPeasy.Data/README.md)** — EF Core/SQLite implementation of
  Core's repository interfaces, plus the `DbContext`, migrations, and learning-query filter/sort
  extensions (`LearningPriority.New/Old/...`). 41 tests in `EasyPeasy.Data.Tests`, against a real
  in-memory SQLite connection (not EF Core's `InMemory` provider — that doesn't catch LINQ-to-SQL
  translation bugs).

- **[EasyPeasy.Business](../../EasyPeasy.Business/README.md)** — the domain service layer
  (`WordService`, `UnitService`, etc.), implementing Core's service interfaces. **Depends only on
  Core's repository interfaces, not on EasyPeasy.Data directly** — see
  [key-decisions.md](../Decisions/key-decisions.md) for why that matters. 29 tests in
  `EasyPeasy.Business.Tests`.

- **[EasyPeasy.Cache](../../EasyPeasy.Cache/README.md)** — a small in-memory "working set"
  cache (current unit, active session's words) for `EasyPeasy.App`, backed by
  `IStorageService` for persisting *which* ids are selected across app restarts. Not a
  general-purpose cache — no expiry/eviction. 19 tests in `EasyPeasy.Cache.Tests`.

- **[EasyPeasy.App](../../EasyPeasy.App/Services/README.md)** — the MAUI Blazor Hybrid app
  itself, and the composition root: it's the only project that references both `EasyPeasy.Data`
  and `EasyPeasy.Business`, wiring the former's concrete repositories to the latter's interfaces
  via DI in `MauiProgram.cs`. The linked README covers `Services/` (the app-local, non-Razor
  service layer — MAUI platform glue, TTS, pronunciation checking, markdown rendering, spaced
  repetition rating); the Razor components/pages themselves aren't documented yet. 243 tests in
  `EasyPeasy.App.Tests` cover the pure-logic subset of `Services/` (see that README's Testing
  section for why the test project is structured unusually).

## Supporting / peripheral

- **[EasyPeasy.ContentTools](../../EasyPeasy.ContentTools/README.md)** — a personal console tool
  for authoring course content: edits real course-ZIP archives that get imported straight into the
  running app via its own "Import Course ZIP" feature. Depends only on `EasyPeasy.Core`; not part
  of the running app, no automated tests (each module class does real file I/O against a specific
  personal archive — see the linked README for why that isn't meaningfully unit-testable).

## Reusable infrastructure (`MukhaLab.*`)

Generic libraries with zero EasyPeasy-specific code — named `MukhaLab.*` rather than
`EasyPeasy.*` specifically because they're meant to be reusable across unrelated projects (see
[key-decisions.md](../Decisions/key-decisions.md) for the naming rationale).

- **MukhaLab.SelectQueryParameters** — a generic filter/sort/paging query-parameter model plus
  `ApplyQueryParameters()`, an `IQueryable<T>` extension that translates them into a LINQ
  expression (SQL-translatable, not just LINQ-to-Objects). 53 tests.
- **MukhaLab.Database** — generic `BaseRepository<T, TContext>` / `BaseWithGuidRepository` /
  `BaseService<T, TModel>` built on top of `SelectQueryParameters`, plus per-user scoping
  (`IUserContext`) and `EntityNotFoundException`-based not-found handling. 47 tests, against a real
  SQLite in-memory connection.
- **MukhaLab.LoggerExtensionDelegate** — `FastXMessage`/`BeginTimedScope` helpers on top of
  `ILogger` for low-overhead structured logging. Not currently used anywhere in the solution — a
  library, not (yet) a dependency. 28 tests.
- **MukhaLab.BrowserConsoleLogger** — an `ILoggerProvider` that forwards log entries to the
  browser's `console.log`/`console.error`/etc. via `IJSRuntime`, for debugging the Blazor Hybrid
  WebView from browser DevTools. Actively used by `EasyPeasy.App` in DEBUG builds. 32 tests.

## Documentation (this project)

- **EasyPeasy.Docs** — architecture diagrams (`Diagrams/`), developer guides (`Guides/`), and the
  architectural decisions log (`Decisions/`). Not a buildable/runnable project in any meaningful
  sense — it's a plain `net9.0` SDK project used only so the `.md`/`.puml` files show up as a
  first-class node in the solution (Visual Studio's Solution Explorer, `dotnet sln list`, etc.)
  rather than living as untracked loose files outside any project.
