# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-10-24

### Added

- Live timer for a task you type in after the app starts.
- Keys to pause or resume (`P`), switch task (`S`), start a new task (`N`), show today's summary (`T`) and quit (`Q`).
- Task picker with search, listing tasks from the last 7 days with the most recently used first.
- Task names are free text, matched case-insensitively and ignoring surrounding spaces.
- Today's summary with each task's interval count, first start, last end and total time, plus the grand total and number of tasks. A list of every interval is also available.
- One JSON log per day in `%USERPROFILE%\.TaskTracker\logs\yyyy-MM-dd.json`, grouped by task, with the start, end and duration of each interval.
- The running interval is saved immediately, every minute, on `Ctrl+C` and when the console window is closed, so at most about one minute is lost if the app is killed.
- Time while the PC is asleep (more than 5 minutes) is not counted.
- Setting the system clock back no longer causes negative durations.
- Intervals that run past midnight are split between the two days' log files.
- Saving retries when antivirus or sync tools briefly lock the log. A save that still fails shows a warning and is retried a minute later instead of crashing the app.
- Only one copy of the app can run at a time for each Windows user.
- Standalone single-file `TaskTracker.exe` built with the **publish (single exe)** VS Code task. It doesn't need .NET installed, and it has product details and an icon.
- Optional self-signed code signing with the **publish + sign** task, plus scripts to create, use and trust the certificate. Signing is not required to distribute the exe.
- VS Code build, test and publish tasks, an F5 debug configuration and an xUnit test suite.

[unreleased]: https://github.com/vlkozl/tasktracker/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/vlkozl/tasktracker/releases/tag/v1.0.0
