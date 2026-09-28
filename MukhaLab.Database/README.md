# MukhaLab.Database

Generic repository/service base classes for EF Core: `BaseRepository<T, TContext>` and
`BaseService<TEntity, TModel>` give every entity CRUD, batch operations, dynamic querying
(via [`MukhaLab.SelectQueryParameters`](../MukhaLab.SelectQueryParameters)), pagination, per-user
row scoping, and transactions — without re-implementing the same EF Core boilerplate for every
entity in the app.

- **Target framework:** `net9.0`
- **Namespace:** `MukhaLab.Database`
- **Dependencies:** `AutoMapper` (15.1.3), `Microsoft.EntityFrameworkCore` (9.0.7), project reference to [`MukhaLab.SelectQueryParameters`](../MukhaLab.SelectQueryParameters)
- **Used by:** every repository in [`EasyPeasy.Data/Repositories`](../EasyPeasy.Data/Repositories) and every service in [`EasyPeasy.Business/Services`](../EasyPeasy.Business/Services)

## Table of contents

- [Project layout](#project-layout)
- [Core concepts](#core-concepts)
- [Quick start](#quick-start)
- [Dynamic querying & pagination](#dynamic-querying--pagination)
- [Per-user row scoping](#per-user-row-scoping)
- [Error handling](#error-handling)
- [Transactions](#transactions)
- [GUID identity (`BaseWithGuidRepository`/`BaseWithGuidService`)](#guid-identity-basewithguidrepositorybasewithguidservice)
- [Real usage in EasyPeasy](#real-usage-in-easypeasy)
- [Known issues & risks](#known-issues--risks)
- [Suggested improvements](#suggested-improvements)
- [Troubleshooting](#troubleshooting)

## Project layout

```
MukhaLab.Database/
├── AbstractEntity.cs             # base class for EF entities: int Id
├── AbstractModel.cs              # base class for business-layer models: int Id
├── IGuidRecord.cs                # marker interface: RecordGuid (stable GUID identity)
├── IUserContext.cs               # current-user id source for per-user row scoping
├── AnonymousUserContext.cs       # IUserContext for apps without per-user authorization
├── PaginationInfo.cs             # TotalCount / TotalPages returned by GetPaginationInfoAsync
├── EntityNotFoundException.cs    # thrown for every "not found" / ownership-check failure
├── IBaseRepository.cs            # repository contract: CRUD, batch ops, dynamic query, paging
├── BaseRepository.cs             # EF Core implementation of IBaseRepository<T>
├── IBaseService.cs               # service contract: entity<->model mapping over IBaseRepository<T>
├── BaseService.cs                # AutoMapper-backed implementation of IBaseService<TModel>
├── IBaseWithGuidRepository.cs    # IBaseRepository<T> + RecordGuid-based lookups
├── BaseWithGuidRepository.cs     # BaseRepository<T,TContext> + RecordGuid-based lookups
├── IBaseWithGuidService.cs       # IBaseService<TModel> + RecordGuid-based lookups
└── BaseWithGuidService.cs        # BaseService<TEntity,TModel> + RecordGuid-based lookups
```

## Core concepts

| Type | Role |
|---|---|
| `AbstractEntity` | Base class for EF Core entities. Provides `int Id` (`[Key]`, column `"id"`). Required by `IBaseRepository<T>`/`BaseRepository<T, TContext>` (`T : AbstractEntity`). |
| `AbstractModel` | Base class for business-layer models. Provides `int Id`. Not required by `IBaseService<TModel>` (only `TEntity` must derive from `AbstractEntity`), but used by convention throughout the app. |
| `IBaseRepository<T>` / `BaseRepository<T, TContext>` | Generic CRUD + batch + dynamic-query + paging repository. Each operation opens and disposes its own `TContext` via `IDbContextFactory<TContext>` — thread-safe, no shared/ambient context. Enforces per-user row scoping (when configured) on every method, including by-key lookups/updates/deletes. |
| `IBaseService<TModel>` / `BaseService<TEntity, TModel>` | Maps between `TEntity` (persistence) and `TModel` (business layer) with AutoMapper, delegating to an `IBaseRepository<TEntity>`. Logs at `Debug`/`Information` on success, `Error` before rethrowing on failure. |
| `IUserContext` / `AnonymousUserContext` | Supplies the current user's id for per-user row scoping. `AnonymousUserContext` is the implementation for apps without per-user authorization — see [Per-user row scoping](#per-user-row-scoping) for an important caveat. |
| `PaginationInfo` | `TotalCount` + `TotalPages` for a filtered query, returned by `GetPaginationInfoAsync`. |
| `EntityNotFoundException` | Thrown consistently whenever a lookup/update/delete can't find a matching, owned row (including concurrent modification/deletion) — see [Error handling](#error-handling). |
| `IGuidRecord` | Marker interface (`Guid RecordGuid`) for entities/models needing a stable identity independent of the auto-incrementing `int Id` — e.g. reconciling records across a re-import. |
| `IBaseWithGuidRepository<T>` / `BaseWithGuidRepository<T, TContext>` | `IBaseRepository<T>`/`BaseRepository<T,TContext>` plus `FindAsync(Guid)` and `CheckExistingGuidsAsync`. |
| `IBaseWithGuidService<TModel>` / `BaseWithGuidService<TEntity, TModel>` | Same, at the service layer. |

## Quick start

**1. Define the entity and model:**

```csharp
// EF Core entity
public class WordEntity : AbstractEntity, IAuditInfo
{
    public string Word { get; set; } = string.Empty;
    public string? Translation { get; set; }
    public int UnitId { get; set; }
    public UnitEntity? Unit { get; set; }
}

// Business-layer model
public class WordModel : AbstractModel
{
    public string Word { get; set; } = string.Empty;
    public string? Translation { get; set; }
}
```

**2. Define the repository, extending `BaseRepository<T, TContext>`:**

```csharp
public interface IWordRepository : IBaseRepository<WordEntity>
{
    Task<List<WordEntity>> GetByUnitAsync(int unitId, string[]? includes = null);
}

public class WordRepository : BaseRepository<WordEntity, EasyPeasyDbContext>, IWordRepository
{
    public WordRepository(IDbContextFactory<EasyPeasyDbContext> contextFactory, IUserContext? userContext = null)
        : base(contextFactory, userContext) { }

    // Custom queries use contextFactory directly, same pattern as the base class:
    public async Task<List<WordEntity>> GetByUnitAsync(int unitId, string[]? includes = null)
    {
        await using var ctx = await contextFactory.CreateDbContextAsync();
        IQueryable<WordEntity> query = ctx.Words.Where(w => w.UnitId == unitId);
        if (includes is not null)
            foreach (var include in includes)
                query = query.Include(include);
        return await query.AsNoTracking().ToListAsync();
    }
}
```

**3. Define the service, extending `BaseService<TEntity, TModel>`:**

```csharp
public interface IWordService : IBaseService<WordModel> { }

public class WordService : BaseService<WordEntity, WordModel>, IWordService
{
    public WordService(IWordRepository repository, IMapper mapper, ILogger<WordService> logger)
        : base(repository, mapper, logger) { }
}
```

**4. Register with DI:**

```csharp
services.AddScoped<IWordRepository, WordRepository>();
services.AddScoped<IWordService, WordService>();
services.AddScoped<IUserContext, AnonymousUserContext>(); // apps without per-user auth
```

**5. Use it:**

```csharp
var word = await wordService.CreateAsync(new WordModel { Word = "apple" });
var page = await wordService.GetAllAsync(includes: new[] { nameof(WordEntity.Unit) });
var updated = await wordService.UpdateAsync(word.Id, word);
await wordService.DeleteAsync(word.Id);
```

Every CRUD/batch method from `IBaseRepository<T>`/`IBaseService<TModel>` (`AddAsync`, `AddRangeAsync`,
`UpdateAsync`, `UpdateRangeAsync`, `RemoveAsync`/`DeleteAsync`, `RemoveRangeAsync`/`DeleteRangeAsync`,
`FindAsync`/`GetByIdAsync`, `FindManyAsync`/`GetByIdsAsync`, `CountAsync`) is available on
`WordRepository`/`WordService` for free — only entity-specific queries need custom code.

## Dynamic querying & pagination

`GetAsync(QueryParameters, ...)` / `GetAllAsync(QueryParameters, ...)` and
`GetPaginationInfoAsync(QueryParameters, ...)` accept a `QueryParameters` from
`MukhaLab.SelectQueryParameters` — see that library's
[README](../MukhaLab.SelectQueryParameters/README.md) for the full filter/sort/paging syntax.

```csharp
var parameters = new QueryParameters
{
    PageNumber = 1,
    RowCount = 20,
    Filters = new List<FilterParameter>
    {
        new FilterParameter { Field = "Word", Operation = FilterOperation.Contains, DataType = FilterDataType.String, Value = "app" }
    },
    Sort = new List<SortDescriptor> { new SortDescriptor { Field = "Word", Direction = SortDirection.Asc } }
};

var page = await wordService.GetAllAsync(parameters);
var pagination = await wordService.GetPaginationInfoAsync(parameters); // TotalCount, TotalPages for the same filters
```

`BaseRepository<T, TContext>` builds the pagination count by re-running the same filters with paging
stripped out (`BuildSelectQuery(ctx, parameters, withoutPagination: true, ...)`), so
`GetPaginationInfoAsync`'s `TotalCount` always matches what `GetAsync(parameters, ...)` would return
across all pages combined. `CountAsync()` (no parameters) is a separate, simpler method that always
counts the whole (optionally per-user-scoped) table — it does not accept filters; use
`GetPaginationInfoAsync` for a filtered count.

## Per-user row scoping

`BaseRepository<T, TContext>` can automatically scope every operation to rows owned by the current
user:

```csharp
public class NoteRepository : BaseRepository<NoteEntity, AppDbContext>
{
    public NoteRepository(IDbContextFactory<AppDbContext> contextFactory, IUserContext userContext)
        : base(contextFactory, userContext)
    {
        ConfigureUserIdField(new[] { "UserId" }); // or "Owner.UserId" for a collection navigation
    }
}
```

With this configured, **every** method applies the filter (`x.UserId == userContext.GetCurrentUserId()`,
OR-combined across every configured path):

- `GetAsync`/`GetAllAsync`, `CountAsync`, `GetPaginationInfoAsync` — rows the current user doesn't
  own are simply excluded from results/counts.
- `FindAsync`/`GetByIdAsync`, `FindManyAsync`/`GetByIdsAsync` — a row that exists but isn't owned by
  the current user is treated the same as a row that doesn't exist (returns `null`/omits it from the
  result, exactly like a genuine miss).
- `UpdateAsync`/`UpdateRangeAsync`, `RemoveAsync`/`DeleteAsync`, every `RemoveRangeAsync` overload —
  ownership is verified *before* the write; if the entity doesn't exist or isn't owned by the current
  user, the operation throws `EntityNotFoundException` instead of silently succeeding against
  another user's row.

The one gap: `FindAsync(params object[] keyValues)`'s composite-key branch (used when the primary
key isn't a single `int`) cannot be composed with the filter, since it goes through a raw
`DbSet.FindAsync(object[])` call. `AbstractEntity`'s single `int Id` primary key means every current
entity in this codebase goes through the filtered, single-int-key branch instead.

Scoping is active only once **both** conditions hold — supplying a non-null `IUserContext` alone is
not enough:
1. A non-null `IUserContext` was passed to the constructor.
2. `ConfigureUserIdField` was called with at least one property path.

> **`AnonymousUserContext` does not disable this feature.** It is a working `IUserContext` that
> reports `Guid.Empty` as "the current user" — if you also call `ConfigureUserIdField`, every query
> gets scoped to rows literally owned by `Guid.Empty`. To genuinely disable per-user scoping, either
> don't call `ConfigureUserIdField` at all, or construct the repository with `userContext: null`.
> EasyPeasy.App registers `AnonymousUserContext` but never calls `ConfigureUserIdField` anywhere,
> so scoping is effectively inactive there today — see [Real usage in EasyPeasy](#real-usage-in-easypeasy).

## Error handling

Every "not found" condition across `BaseRepository<T, TContext>` and `BaseService<TEntity, TModel>`
throws `EntityNotFoundException` — a single, consistently-used exception type for:

- A lookup-by-key that doesn't exist.
- An update/delete targeting an entity that doesn't exist or isn't owned by the current user (see
  [Per-user row scoping](#per-user-row-scoping)).
- A concurrent modification or deletion detected by EF Core (`DbUpdateConcurrencyException` is
  caught during `UpdateAsync`/`UpdateRangeAsync`/`RemoveAsync`/every `RemoveRangeAsync` overload and
  rethrown as `EntityNotFoundException` with the original exception as `InnerException`).

```csharp
try
{
    await wordService.UpdateAsync(id, model);
}
catch (EntityNotFoundException)
{
    // id doesn't exist, isn't owned by the current user, or was deleted concurrently
}
```

Note that `BaseRepository<T, TContext>` does not (and, without an EF Core-configured concurrency
token on the entity, cannot) detect a concurrent update that changes different fields of the same
row without deleting it — only a delete-or-nonexistence conflict is caught this way. See
[Known issues #1](#known-issues--risks) for the remaining gap.

## Transactions

`ExecuteInTransactionAsync` runs multiple operations against a single `DbContext`/transaction:

```csharp
await repository.ExecuteInTransactionAsync(async ctx =>
{
    ctx.Set<WordEntity>().Add(word);
    await ctx.SaveChangesAsync();

    ctx.Set<ExampleEntity>().AddRange(examples);
    await ctx.SaveChangesAsync();
});
```

**Use the `ctx` parameter directly** for every read/write inside `operation`. Calling back into
other repository methods (e.g. `repository.AddAsync(...)`) from inside the callback does **not**
participate in the transaction — each repository method opens its **own** `DbContext` via the
factory, on a separate connection, regardless of an ambient transaction elsewhere.

A derived repository can reuse the base class's single-entity mutation logic against the supplied
`ctx` via three `protected` helpers, instead of duplicating `ctx.Set<T>().Add/Update/Remove(...)`:

```csharp
protected class WordRepository : BaseRepository<WordEntity, EasyPeasyDbContext>, IWordRepository
{
    public async Task ImportWordWithExamplesAsync(WordEntity word, IEnumerable<ExampleEntity> examples)
    {
        await ExecuteInTransactionAsync(async ctx =>
        {
            this.Add(ctx, word);           // protected helper: ctx.Set<T>().Add(word), no save
            await ctx.SaveChangesAsync();

            ctx.Set<ExampleEntity>().AddRange(examples);
            await ctx.SaveChangesAsync();
        });
    }
}
```

`Add`/`Update`/`Remove` (protected, `TContext ctx, T entity`, no `Async` suffix — they don't save)
do **not** perform the per-user ownership check that `UpdateAsync`/`RemoveAsync` do; a derived class
using them directly inside a transaction is responsible for authorization itself.

## GUID identity (`BaseWithGuidRepository`/`BaseWithGuidService`)

For entities that need a stable identity independent of the database-generated `int Id` — e.g.
matching records across a re-import — implement `IGuidRecord` and derive from the `*WithGuid*` base
classes instead:

```csharp
public class CourseEntity : AbstractEntity, IGuidRecord
{
    public Guid RecordGuid { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
}

public interface ICourseRepository : IBaseWithGuidRepository<CourseEntity> { }

public class CourseRepository : BaseWithGuidRepository<CourseEntity, EasyPeasyDbContext>, ICourseRepository
{
    public CourseRepository(IDbContextFactory<EasyPeasyDbContext> contextFactory, IUserContext? userContext = null)
        : base(contextFactory, userContext) { }
}
```

This adds `FindAsync(Guid, ...)` and `CheckExistingGuidsAsync(IEnumerable<Guid>, ...)` on top of
everything `IBaseRepository<T>` already provides — useful for "does this record already exist"
checks during import without relying on the (re-import-unstable) `int Id`.

## Real usage in EasyPeasy

- [`WordRepository`](../EasyPeasy.Data/Repositories/WordRepository.cs) : `BaseRepository<WordEntity, EasyPeasyDbContext>` — adds entity-specific queries (`GetByUnitAsync`, `GetForLearningAsync`, ...) alongside the inherited CRUD surface.
- [`CourseRepository`](../EasyPeasy.Data/Repositories/CourseRepository.cs) : `BaseWithGuidRepository<CourseEntity, EasyPeasyDbContext>` — a minimal repository with no extra methods, relying entirely on the base classes.
- [`WordService`](../EasyPeasy.Business/Services/WordService.cs) : `BaseService<WordEntity, WordModel>` — adds domain methods (`GetForLearningAsync`, `UpdateWordRateAsync`, ...) on top of the inherited CRUD surface; follows the base class's try/catch-log-rethrow pattern in its own methods too.
- [`AddEasyPeasyRepositories`](../EasyPeasy.Data/Extensions/ServiceCollectionExtensions.cs) registers every repository as `Scoped`.
- [`MauiProgram.cs`](../EasyPeasy.App/MauiProgram.cs) registers `services.AddScoped<IUserContext, AnonymousUserContext>();` — no repository in this app currently calls `ConfigureUserIdField`, so per-user scoping is registered but inactive (EasyPeasy is a single-user, on-device app).

## Known issues & risks

The items below are still open. Several previously-listed issues have been fixed (per-user scoping
now applies to every method including by-key lookups/updates/deletes; concurrent
deletion/modification during `UpdateAsync`/`RemoveAsync` now throws `EntityNotFoundException`
instead of silently succeeding or being missed; `ExecuteInTransactionAsync` now has `Add`/`Update`/`Remove`
composition helpers; `AnonymousUserContext` replaces the misleadingly-named `NullUserContext`;
`RemoveRangeAsync(IEnumerable<T>, ...)` is now part of `IBaseRepository<T>`; every "not found" path
uses `EntityNotFoundException` consistently; developer-facing comments and XML docs are English
throughout; `IBaseRepository<T>`/`BaseRepository<T, TContext>` now require `T : AbstractEntity` and
use `e.Id` directly instead of `EF.Property<int>(e, "Id")`).

1. **No detection of concurrent field-level updates.** `UpdateAsync`/`UpdateRangeAsync` now catch
   `DbUpdateConcurrencyException` (thrown by EF Core when a row a write targets was concurrently
   deleted) and surface it as `EntityNotFoundException`, and pre-check ownership when per-user
   scoping is active — but without an EF Core-configured concurrency token (e.g. a `RowVersion`
   column) on the entity, two concurrent updates that both target an existing, still-owned row still
   silently overwrite each other field-for-field; only deletion-during-update is caught. Adding a
   real concurrency token would require entity/DbContext model changes (a schema migration) outside
   this library's scope — see [Suggested improvements](#suggested-improvements).
2. **Duplicated per-user-filter application logic.** `BuildSelectQuery(ctx, includes)` and
   `CountAsync()` each independently apply `enableUserFiltering ? IncludeUserIdFilter(query) : query`
   — the same two lines, copy-pasted rather than shared.
3. **`BaseService`'s per-method `try { ... } catch (Exception ex) { LogError; throw; }` is repeated
   verbatim across all ~13 methods** with no differentiated handling — it logs and rethrows
   unconditionally, so it adds a log line but changes no behavior. The same pattern also appears in
   hand-written services like `WordService.UpdateWordRateRangeAsync`, suggesting it's an established
   house convention rather than base-class-specific boilerplate.
4. **Five `[Obsolete]` synchronous wrapper methods** (`GetAll`, `GetById`, `Create`, `Update`,
   `Delete`) use `.GetAwaiter().GetResult()` (sync-over-async). Confirmed unused anywhere in the
   solution today.
5. **`BaseService`'s log messages remain in Ukrainian** (e.g. `_logger.LogDebug("Retrieving all
   records...")` calls use English text after this pass, but check current source for any missed
   spots as the codebase evolves) — kept as most log text was already Ukrainian project-wide;
   exception messages were normalized to `EntityNotFoundException` with English text as part of this
   pass, but general logging language wasn't standardized beyond that.

## Suggested improvements

- **For #1:** if optimistic concurrency for field-level conflicts matters, add an opt-in convention
  (e.g. an `IConcurrencyAware`/`RowVersion` marker) that a derived `DbContext` can configure as an EF
  Core concurrency token per entity; `BaseRepository` already surfaces `DbUpdateConcurrencyException`
  as `EntityNotFoundException`, so no repository-layer change would be needed once a token exists —
  only the entity/`DbContext` model and a migration.
- **For #2:** factor the two-line "apply user filter" snippet into a small private helper reused by
  both `BuildSelectQuery` and `CountAsync`.
- **For #3:** if centralizing is desired, a single generic wrapper (e.g.
  `ExecuteLoggedAsync(string operationName, Func<Task<T>> body)`) could replace the repeated
  try/catch blocks; weigh this against the value of keeping each method's log messages contextual
  and easy to grep.
- **For #4:** delete the five obsolete synchronous wrappers — confirmed unused anywhere in the
  solution, so removing them is pure dead-code cleanup with no migration needed.
- **For #5:** translate `BaseService`'s remaining log messages to English (or explicitly adopt
  Ukrainian as house style for log text specifically) for full consistency with the English-only
  code-comment convention.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `EntityNotFoundException` from `UpdateAsync`/`RemoveAsync`/etc. even though the id looks right | Per-user filtering is active (`ConfigureUserIdField` was called) and the entity isn't owned by the current user, or it was concurrently deleted — see [Per-user row scoping](#per-user-row-scoping) and [Error handling](#error-handling). |
| Query results include rows belonging to other users | `ConfigureUserIdField` was never called — per-user scoping is off by default even with a non-null `IUserContext`. |
| Registering `AnonymousUserContext` didn't disable per-user filtering | Expected — see [Per-user row scoping](#per-user-row-scoping); it supplies `Guid.Empty`, not "no filter". |
| Two concurrent edits to different fields of the same row both "succeeded" but one was lost | Expected without a configured concurrency token — see [Known issues #1](#known-issues--risks). |
| Writes made inside an `ExecuteInTransactionAsync` callback (via a repository method) aren't rolled back with the rest | The callback called back into a repository method (e.g. `AddAsync`) instead of using the supplied `ctx` or the `Add`/`Update`/`Remove` helpers — see [Transactions](#transactions). |
| `EntityNotFoundException: Entities of type X were not all found` from `RemoveRangeAsync`/batch delete | One or more requested ids don't exist or aren't owned by the current user; the batch delete is all-or-nothing. |
| Compile error after changing an entity to not derive from `AbstractEntity` | `IBaseRepository<T>`/`BaseRepository<T, TContext>` require `T : AbstractEntity`. |
