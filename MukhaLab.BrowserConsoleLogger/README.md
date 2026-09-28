# MukhaLab.BrowserConsoleLogger

Mirrors application log output to the JavaScript console of the WebView hosting a Blazor Hybrid
(MAUI) or Blazor WebAssembly app. It exposes **two independent, complementary ways** to get a
message into the browser console:

1. A standard `ILoggerProvider` (`AddBrowserConsole()`) that mirrors *every* `ILogger<T>` call in
   the app — including from services and background code with no direct access to `IJSRuntime`.
2. A directly-injectable `IBrowserConsoleService` for one-off logging calls from Razor components
   that already have JS interop available.

- **Target framework:** `net9.0`
- **Namespace:** `MukhaLab.BrowserConsoleLogger`
- **Dependencies:** `Microsoft.Extensions.Logging`, `Microsoft.JSInterop` (9.0.8)
- **Used by:** [`EasyPeasy.App`](../EasyPeasy.App) (MAUI Blazor Hybrid) — see
  [Real usage in EasyPeasy.App](#real-usage-in-easypeasyapp).

## Table of contents

- [Two logging paths](#two-logging-paths)
- [Installation](#installation)
- [Path 1 — `ILoggerProvider` (`AddBrowserConsole`)](#path-1--iloggerprovider-addbrowserconsole)
- [Path 2 — `IBrowserConsoleService`](#path-2--ibrowserconsoleservice)
- [Real usage in EasyPeasy.App](#real-usage-in-easypeasyapp)
- [Known limitations & gotchas](#known-limitations--gotchas)
- [Troubleshooting](#troubleshooting)

## Two logging paths

| | `AddBrowserConsole()` (`BrowserConsoleLogger`) | `IBrowserConsoleService` (`BrowserConsoleService`) |
|---|---|---|
| Wired into | `ILoggingBuilder` — becomes part of every `ILogger<T>` | Injected directly where needed |
| Delivery | Queued, delivered asynchronously in the background | Immediate `await`, no queue |
| Works before JS interop is ready? | Yes — entries queue up and flush once `IJSRuntime` becomes available | No — throws/falls back if JS interop isn't ready yet |
| Failure behavior | Retries on known transient errors; any other exception falls back to `Debug.WriteLine` for that entry (never silently dropped) | Never drops — falls back to `Debug.WriteLine` on any exception |
| Queue growth | Bounded — oldest entries are discarded past 1000 pending | N/A (no queue) |
| Registration | `builder.Logging.AddBrowserConsole()` | `services.AddBrowserConsoleService()` |
| Best for | Blanket, app-wide diagnostic logging (typically DEBUG builds only) | Targeted logging calls inside Razor components |

Both paths ultimately call the same `IJSRuntime` console methods
(`console.log`/`info`/`warn`/`error`/`debug`/`trace`) and prefix messages with an
`HH:mm:ss.fff` timestamp.

## Installation

```csharp
using MukhaLab.BrowserConsoleLogger;
```

## Path 1 — `ILoggerProvider` (`AddBrowserConsole`)

Register once at startup:

```csharp
builder.Logging.AddDebug();
builder.Logging.AddBrowserConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
```

From then on, any `ILogger<T>` resolved through DI mirrors its output to the browser console —
no code changes needed at call sites:

```csharp
public class WordService(ILogger<WordService> logger)
{
    public async Task ImportAsync()
    {
        logger.LogInformation("Import started"); // also appears in the WebView's console.info
    }
}
```

`AddBrowserConsole()` registers `BrowserConsoleLoggerProvider` as a singleton `ILoggerProvider`
(`BrowserConsoleLoggerExtensions.cs`). The provider owns one `BrowserConsoleLogQueue` (the shared
delivery pipeline) and creates one `BrowserConsoleLogger` per category name (cached in a
`ConcurrentDictionary`), all sharing that provider instance's queue.

### How delivery works

`BrowserConsoleLogger.Log(...)` can be called from anywhere — including before `IJSRuntime` is
available (app startup, background threads, prerendering). It never calls `IJSRuntime` directly.
Instead, for every call it:

1. Writes the formatted line to `System.Diagnostics.Debug` immediately (always succeeds).
2. Hands a `PendingLogEntry` to the provider's `BrowserConsoleLogQueue.Enqueue(...)`, which adds it
   to an internal `ConcurrentQueue` capped at 1000 pending entries (oldest entries are dropped once
   the cap is exceeded, so recent logs are prioritized).
3. `Enqueue` fires an unawaited `Task.Run(...)` that tries to drain the queue: it lazily resolves
   and caches an `IJSRuntime` from DI, then flushes up to 50 queued entries per attempt via
   `IJSRuntime.InvokeVoidAsync`. A `System.Threading.Interlocked`-based guard ensures only one flush
   runs at a time per queue.

This design lets `Log()` be non-blocking and safe to call before any WebView/JS runtime exists —
entries simply accumulate (up to the cap) until the first successful flush. If delivery to the
browser console fails for a reason other than "JS interop not ready yet" or "circuit disconnected",
the entry is written to `System.Diagnostics.Debug` as a fallback instead of being silently dropped.

`BrowserConsoleLogQueue` is an instance owned by the `BrowserConsoleLoggerProvider` that created it
— not a process-wide static — so its state doesn't leak across independent provider instances (for
example, across test runs). Note that `ILoggerProvider` registrations are themselves host-level
singletons in ASP.NET Core, so a single Blazor Server host still has exactly one provider — and
therefore one shared queue — for every circuit; this change removes the *unnecessary* AppDomain-wide
sharing (multiple independent providers no longer contend for the same static state), but full
per-user isolation in Blazor Server would still require registering a provider per circuit, which is
outside the scope of this library.

This provider's `IsEnabled(LogLevel)` always returns `true` (except for `LogLevel.None`) — it does
not filter by level itself. Verbosity is controlled entirely through the normal
`ILoggingBuilder` configuration (`SetMinimumLevel`, `AddFilter`), exactly as for any other provider.

## Path 2 — `IBrowserConsoleService`

Inject directly into a Razor component (after JS interop is available, e.g. `OnAfterRenderAsync`
or any event handler) and call it like a normal async logging API:

```razor
@inject IBrowserConsoleService BrowserConsole

@code {
    private async Task LoadAsync()
    {
        try
        {
            var courses = await CourseService.GetAllAsync();
            await BrowserConsole.LogInfoAsync($"Loaded {courses.Count} courses");
        }
        catch (Exception ex)
        {
            await BrowserConsole.LogErrorAsync("Failed to load courses", ex);
        }
    }
}
```

Unlike Path 1, each call directly `await`s `IJSRuntime.InvokeVoidAsync` — there is no queue and no
retry. If the call throws for any reason, `BrowserConsoleService` catches it and falls back to
`System.Diagnostics.Debug.WriteLine`, so a message is never silently lost, though it may end up
only in the Debug console instead of the browser console.

Register it with the matching extension method (mirrors `AddBrowserConsole()`):

```csharp
builder.Services.AddBrowserConsoleService();
```

## Real usage in EasyPeasy.App

[`MauiProgram.cs`](../EasyPeasy.App/MauiProgram.cs) wires up both paths:

```csharp
// Registered unconditionally (both DEBUG and Release):
builder.Services.AddBrowserConsoleService();

// Registered only for DEBUG builds — see ConfigureLogging(builder):
#if DEBUG
builder.Services.AddBlazorWebViewDeveloperTools();
builder.Logging.AddDebug();
builder.Logging.AddBrowserConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
#else
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
#endif
```

**`AddBrowserConsole()` is DEBUG-only in this app** — it is a local-development diagnostic tool
(open the WebView developer tools to see the mirrored output), not part of the Release logging
pipeline.

`IBrowserConsoleService` is injected globally via
[`Components/_Imports.razor`](../EasyPeasy.App/Components/_Imports.razor)
(`@inject IBrowserConsoleService BrowserConsole`), so every page/component can call
`BrowserConsole.LogInfoAsync(...)` / `LogErrorAsync(...)` without injecting it individually. It is
used extensively across `Components/Pages/Courses/*.razor` and `Components/Pages/Drilling/**/*.razor`
for two recurring patterns:

```csharp
// 1. Diagnostic snapshot after loading/saving an entity (not awaited — fire and forget):
BrowserConsole.LogInfoAsync($"✅ Word : {JsonSerializer.Serialize(Word)}");

// 2. Error reporting in a catch block (awaited):
catch (Exception ex)
{
    await BrowserConsole.LogErrorAsync("Failed to load courses", ex);
}
```

Both `await`ed and fire-and-forget (unawaited) call styles are used in the app; prefer `await`ing
when the surrounding method is already `async` and you want the log guaranteed to be sent before
continuing (e.g. right before rethrowing or navigating away).

## Known limitations & gotchas

- **Still not fully multi-user-safe in Blazor Server.** `BrowserConsoleLogQueue` is now owned per
  `BrowserConsoleLoggerProvider` instance rather than being a process-wide static, which removes
  unnecessary sharing across independent provider instances. However, `ILoggerProvider`
  registrations are themselves host-level singletons in ASP.NET Core, so a single Blazor Server host
  still has exactly one provider — and therefore one shared queue and one cached `IJSRuntime` — for
  every circuit. This library targets single-user client hosts (MAUI Blazor Hybrid, Blazor
  WebAssembly), where there is only ever one `IJSRuntime` per process; true per-circuit isolation for
  Blazor Server would require registering a provider per circuit, which this library does not do.
- **Console-side formatting is minimal.** Objects passed as `Exception` are rendered with
  `Exception.ToString()` (full stack trace) appended after a newline; there is no structured/JSON
  console output, only single formatted strings.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Nothing appears in the browser/WebView console, but Debug output shows the message | `IJSRuntime` was not yet resolvable when the queue tried to flush, or delivery failed with a non-retryable exception (in which case the entry's fallback line was also written to Debug — look for a "failed to deliver a log entry" line just above it). |
| Log entries seem delayed | `Log()` never sends synchronously — delivery happens on a background `Task.Run` once `IJSRuntime` is cached; expect a short async delay, not immediate output. |
| Some very old entries are missing after a burst of logging | The pending queue is capped at 1000 entries; if delivery can't keep up, the oldest entries are dropped to make room for new ones. |
| `IBrowserConsoleService` throws or logs nothing via JS but writes to Debug output | JS interop wasn't ready at the call site (e.g. called too early in a component's lifecycle, before first render); check the `Debug.WriteLine` fallback line for the underlying exception message. |
| `AddBrowserConsole()` output missing in a Release build | Expected in this app — it's registered only under `#if DEBUG` in `MauiProgram.cs`. |
| A high volume of early-startup logs never appear in the browser console | The pending-log queue only starts draining once `IJSRuntime` is resolvable; entries logged before that point queue up (up to the 1000-entry cap) and flush together once interop is ready. |
