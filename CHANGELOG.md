# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `AGENTS.md` with build, test and architecture notes for AI coding agents working on the repository.

## [1.0.0] - 2026-10-24

### Added

- Status screen showing the current task's time today and the day's total (hours:minutes, updated every minute).
- On start, pick a recent task or type a new one.
- Keys to pause or resume (`P`), switch task (`S`), start a new task (`N`), show today's summary (`T`) and quit (`Q`).
- Task picker with search, listing tasks from the last 7 days with the most recently used first.
- Task names are free text, matched case-insensitively and ignoring surrounding spaces.
- Today's summary with each task's interval count, first start, last end and total time, plus the grand total and number of tasks. Press `I` in the summary to list every interval. The summary is also shown on start, if today already has tasks, and when you quit.
- One JSON log per day in `%USERPROFILE%\.TaskTracker\logs\yyyy-MM-dd.json`, grouped by task, with the start, end and duration of each interval.
- Every start, pause, resume and switch is saved immediately. The running interval is also saved every minute, on quit, on `Ctrl+C` and when the console window is closed, so at most about one minute is lost if the app is killed.
- Logs are written to a temporary file and then swapped in, so a crash never leaves a half-written log.
- Time while the PC is asleep or hibernating for more than 5 minutes is not counted.
- Setting the system clock back never produces negative durations. Small corrections of up to 5 seconds are ignored.
- Intervals that run past midnight are split between the two days' log files.
- Saving retries briefly when antivirus or sync tools lock the log. If a save still fails, tracking continues and the save is retried on the next minute. On quit, a failed save is retried once before the app exits.
- If today's log file is unreadable or corrupt, the app shows the error and exits without changing the file.
- Only one copy of the app can run at a time for each Windows user. A second copy shows a message and exits.
- Standalone 64-bit Windows single-file `TaskTracker.exe` built with the **publish (single exe)** VS Code task. It doesn't need .NET installed, and it has product details and an icon.
- Optional self-signed code signing with the **publish + sign** task, plus scripts to create, use and trust the certificate. Signatures are timestamped, so signing needs internet access. Signing is not required to distribute the exe.
- VS Code build, test and publish tasks, an F5 debug configuration and an xUnit test suite.

[unreleased]: https://github.com/vlkozl/tasktracker/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/vlkozl/tasktracker/releases/tag/v1.0.0
