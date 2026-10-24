# AGENTS.md

Instructions for AI coding agents working on this repository. For what the app does, its keys,
log format and code signing, read [README.md](README.md). This file covers how to change the code safely.

## Project overview

- Windows-only console time tracker: C# / .NET 10 (`net10.0`), UI built with Spectre.Console.
  It relies on Windows behaviour: console window close events, `%USERPROFILE%` paths, and a named mutex.
- Solution: `TaskTracker.slnx` (app: `src/TaskTracker`, tests: `tests/TaskTracker.Tests`).
- Layout of `src/TaskTracker`:
  - `Program.cs`: single-instance mutex, 1-minute heartbeat timer, Ctrl+C/SIGHUP handlers, starts the UI.
  - `Tracking/Tracker.cs`: all time and state logic.
  - `Storage/DayLogStore.cs`: reads and writes the per-day JSON files.
  - `Models/`: `DayLog`, `TaskLog`, `Interval`, which are the JSON shape.
  - `Reporting/DaySummary.cs`: immutable snapshot for display.
  - `UI/ConsoleUi.cs`: Spectre screens and prompts.

## Commands

```powershell
dotnet build
dotnet test
dotnet run --project src/TaskTracker
dotnet publish src/TaskTracker/TaskTracker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

VS Code tasks in `.vscode/tasks.json` wrap the same commands (`build`, `test`, `publish (single exe)`, `publish + sign`).

- Run the interactive app only in a real terminal. It reads single key presses (`Console.ReadKey`/`KeyAvailable`),
  so it can't be driven with redirected stdin. That is also why `.vscode/launch.json` uses `integratedTerminal`.
  Verify behaviour with tests, not by scripting the UI.
- If the user has TaskTracker running, it locks `bin/Debug` and `dotnet build`/`dotnet test` fail with
  MSB3027 (file locked). Don't kill the user's process. Build into another folder instead:
  `dotnet test --artifacts-path <temp dir>`.
- A second instance refuses to start (mutex `Global\TaskTracker-<UserName>`). This is by design.

## Architecture and invariants

- `Tracker` is the only owner of time and state. It is called from the UI thread, the heartbeat timer thread
  and the Ctrl+C/SIGHUP handlers, so every public method takes `_lock` (a `System.Threading.Lock`). Keep it that way.
- Mutate in memory first, then save. `_dirty` marks unsaved state. If a save throws, state stays dirty and the
  next `Tick` writes it.
- `CatchUp()` runs at the start of every operation and handles three cases:
  - a sleep gap (more than `MaxGap`, 5 min);
  - the clock being set back (backsteps of up to 5 s are absorbed);
  - midnight rollover, which splits the open interval at local midnight.
  The only save inside `CatchUp` is the midnight rollover, because the old day must be written before it is
  replaced. It saves only when dirty, so a paused or idle tracker never writes empty day files.
- Zero-length intervals are dropped in `CloseOpen`, never stored.
- Inject `TimeProvider` and get the time through `Tracker.Now()`. Never call `DateTime.Now` directly.
  Timestamps are truncated to whole seconds.
- The heartbeat callback in `Program.cs` catches only `IOException`, `UnauthorizedAccessException` and
  `InvalidDataException`. Any other exception on the timer thread kills the process, so code reached from
  `Tick` must not throw anything else.

## Storage

- One file per day: `%USERPROFILE%\.TaskTracker\logs\yyyy-MM-dd.json`. The JSON is camelCase
  (`JsonSerializerDefaults.Web`) and grouped by task: `tasks[].intervals[]` with `start`/`end`/`durationSeconds`,
  plus a `totalSeconds` for each task. `DayLog.RecalculateTotals()` fills in the redundant totals on save.
- Writes are atomic: write `<file>.tmp`, then `File.Move(..., overwrite: true)`.
- Transient locks (antivirus, sync tools, the search indexer) surface as `IOException` or
  `UnauthorizedAccessException`, so always catch both. `Load` and `Save` retry up to 4 attempts, 75 ms apart.
- The file name is the source of truth for the date. `Load` overwrites `date` and normalizes null lists.
  Invalid JSON becomes `InvalidDataException`.
- Users have existing logs. Changing the JSON shape is a breaking change, so keep it backward compatible
  (old files must still load).

## UI (Spectre.Console)

- `AnsiConsole.Live` draws nothing until `ctx.Refresh()`. Keep the initial `Refresh()` in `WaitForAction`.
- Task names are arbitrary user text. Wrap them in `Markup.Escape` everywhere they appear in markup,
  including `SelectionPrompt.UseConverter`.
- Prompts can't run inside `Live`. The main loop leaves `Live` when a key is pressed, and only then prompts.

## Testing

- xUnit 2 with `Microsoft.Extensions.TimeProvider.Testing`. Add a test for every behaviour change in `Tracker`
  or `DayLogStore`.
- Make time deterministic with `FakeTimeProvider` and `SetLocalTimeZone(TimeZoneInfo.Utc)`.
- `FakeTimeProvider` can't move backwards. For clock-set-back scenarios, use the `ManualClock` helper in `TrackerTests`.
- Simulate the running app with the `Work` helper, which advances 1 minute and calls `Tick` for each minute.
  A single jump of more than 5 minutes triggers the sleep-gap logic.
- Use `TempStore` (a throwaway log directory under `%TEMP%`) and never the real `DayLogStore.DefaultDirectory`.

## Code style

- Match the existing code: file-scoped namespaces, nullable enabled, implicit usings, primary constructors
  where they are already used, the `Lock` type for locks.
- Write short XML doc comments only where they explain *why*.
- Don't add dependencies without a clear reason.

## Scripts

- `scripts/*.ps1` must also run in Windows PowerShell 5.1:
  - no ternary operator and no `??`;
  - `$PSScriptRoot` is empty inside `param()` defaults in 5.1, so resolve defaults in the script body.
- Code signing is optional. It uses a self-signed certificate, and `Sign-Exe.ps1` fails if it can't get a timestamp.
- Never commit private keys (`*.pfx`, `*.p12`, `*.snk` are gitignored). `signing/tasktracker-codesign.cer`
  is the public certificate and is meant to be committed.

## Changelog, versioning, git

- `CHANGELOG.md` follows Keep a Changelog 1.1.0 and SemVer. Add user-facing changes under `## [Unreleased]`,
  using only the Added / Changed / Deprecated / Removed / Fixed / Security headings.
  When releasing, update the link references at the bottom.
- The version is set in `<Version>` in `src/TaskTracker/TaskTracker.csproj`. Releases are tagged `vX.Y.Z`.
- Only `.vscode/tasks.json` and `launch.json` are committed. `bin/`, `obj/`, `publish/`, `.vs/`, `.serena/`
  and `.remember/` are ignored, so don't commit them.
- The repo is used on Windows with `core.autocrlf=true`. LF-to-CRLF warnings from git are harmless.
