# Getting Started

Developer onboarding for the EasyPeasy solution. If you just want an overview of what each
project does, see [solution-structure.md](solution-structure.md) instead.

## Prerequisites

- **.NET 9 SDK** (a .NET 10 SDK works too — it can target `net9.0`; there's no `global.json` pinning
  a specific SDK version).
- **.NET MAUI workloads**: `maui-windows`, `android`, `ios`, `maccatalyst`. Install with:
  ```
  dotnet workload install maui
  ```
  On Windows, building/running the `net9.0-windows10.0.19041.0` target additionally needs the
  Windows App SDK — installed automatically alongside the `maui-windows` workload via Visual
  Studio's ".NET Multi-platform App UI development" component. Only the Windows target is
  practical to build/run/debug locally without a Mac (for iOS/MacCatalyst) or an Android
  emulator/device.

  In practice the app is developed and tested on **Windows and Android only**. The iOS and
  macCatalyst targets have never been built or run, and there is no plan to.
- **PlantUML rendering** (optional, for viewing/editing the `.puml` diagrams in
  [Diagrams/](../Diagrams/)): if using the Visual Studio "PlantUML Editor" extension, set
  Tools → Options → PlantUML → Advanced → Render Type = **Local** — the default hits a dead Azure
  demo server and diagrams silently fail to render.

## Clone and restore

```
git clone <repo-url>
cd EasyPeasy
dotnet restore EasyPeasy.sln
```

## Build

The solution has one MAUI multi-targeted project (`EasyPeasy.App`, targets
`net9.0-android;net9.0-ios;net9.0-maccatalyst` plus `net9.0-windows10.0.19041.0` on Windows) and a
handful of plain `net9.0` class libraries. Building the whole solution builds every target of every
project — on a first build this is slow (MAUI resource processing, resizetizer, etc.):

```
dotnet build EasyPeasy.sln
```

To build just the Windows app (fastest local loop):

```
dotnet build EasyPeasy.App/EasyPeasy.App.csproj -f net9.0-windows10.0.19041.0
```

**Known flaky issue**: this can intermittently fail with
`StaticWebAssets.Publish.targets ... InvalidOperationException: File length for '...compressed\*.gz' is not defined`.
This is a stale/corrupted incremental build cache, not a real code problem — it has recurred several
times during development, unrelated to any specific change. Fix: delete `EasyPeasy.App/obj` and
`EasyPeasy.App/bin`, then rebuild.

## Run

From Visual Studio: set `EasyPeasy.App` as the startup project, pick the "Windows Machine" debug
target, and press F5.

From the CLI (Windows):

```
dotnet build EasyPeasy.App/EasyPeasy.App.csproj -t:Run -f net9.0-windows10.0.19041.0
```

No manual database setup is needed — `appsettings.json` has `Database:AutoMigrate` and
`Database:SeedInitialData` both `true`, so the SQLite file is created and migrated automatically
on first run. That file is `{AppDataPath}/mukhalab.easypeasy.db`; a database left over from before
the rename (`EasyEnglish.db`) is moved to the new name on the first start instead of being ignored,
see [key-decisions.md #13](../Decisions/key-decisions.md).

## Test

Every `.Tests` project uses xUnit and can be run individually:

```
dotnet test EasyPeasy.Core.Tests/EasyPeasy.Core.Tests.csproj
```

Or, since `dotnet test` on a multi-project argument list doesn't reliably run all of them in one
invocation, run each project in a loop:

```bash
for p in EasyPeasy.Core.Tests EasyPeasy.Data.Tests EasyPeasy.Business.Tests \
         EasyPeasy.Cache.Tests EasyPeasy.App.Tests \
         MukhaLab.SelectQueryParameters.Tests MukhaLab.Database.Tests \
         MukhaLab.BrowserConsoleLogger.Tests MukhaLab.LoggerExtensionDelegate.Tests; do
  dotnet test "$p/$p.csproj" || break
done
```

```powershell
$projects = "EasyPeasy.Core.Tests","EasyPeasy.Data.Tests","EasyPeasy.Business.Tests",
            "EasyPeasy.Cache.Tests","EasyPeasy.App.Tests",
            "MukhaLab.SelectQueryParameters.Tests","MukhaLab.Database.Tests",
            "MukhaLab.BrowserConsoleLogger.Tests","MukhaLab.LoggerExtensionDelegate.Tests"
foreach ($p in $projects) { dotnet test "$p/$p.csproj" }
```

`EasyPeasy.Data.Tests`, `MukhaLab.Database.Tests`, and `EasyPeasy.Business.Tests` (for its
integration fixtures) use a real in-memory SQLite connection rather than EF Core's `InMemory`
provider or mocks — no external database server needed, but they do exercise real SQL translation.

See [testing-strategy.md](testing-strategy.md) for what's covered, what isn't, and why.

## Where to go next

- [solution-structure.md](solution-structure.md) — what each project is, with links to its own README
- [conventions.md](conventions.md) — naming/language/DI conventions and known gotchas
- [../Decisions/key-decisions.md](../Decisions/key-decisions.md) — why things are built the way they are
- [../Diagrams/solution-architecture.puml](../Diagrams/solution-architecture.puml) — layered architecture diagram
