# TaskTracker

A 100% vibe coded Windows console app that tracks how much time you spend on tasks.
Type a task name, and a timer starts. You can switch between tasks, pause, and see
today's totals at any time.

## Run

```powershell
dotnet run --project src/TaskTracker
```

In VS Code, press **F5**. The app runs in the integrated terminal, because it needs key presses.

To build a standalone `TaskTracker.exe`, run the **publish (single exe)** task. The exe is written to `publish/`.
It doesn't need .NET installed on the target PC, and it doesn't need to be signed (see [Code signing](#code-signing-optional)).

## Keys

| Key | Action |
|-----|--------|
| `P` | Pause / resume the current task |
| `S` | Switch task: pick a recent task (type to search) or choose **+ New task...** |
| `N` | Type a new task name straight away |
| `R` | Report: stop tracking and show today's summary from the log. `I` lists every interval, `Q` quits, any other key picks a task to continue |
| `Q` | Stop, save, and print today's summary |

## Commands

You don't have to keep the window open. These commands do one thing and exit:

| Command | Action |
|---------|--------|
| `TaskTracker start <task>` | Start a task. If another one is running, it ends first (a switch) |
| `TaskTracker end [<task>]` | End the running task and print its time. With a name, it checks that this task is the one running |
| `TaskTracker report [yyyy-MM-dd]` | Print a day's summary; today without a date |
| `TaskTracker --help` | Commands and keys (also `-h`, `-?`, `/?`) |

Quotes are optional: `TaskTracker start Fix login bug` works. The running task is kept in
`%USERPROFILE%\.TaskTracker\current.json`, and its time is written to the day log when it ends, split at midnight if needed.
Nothing runs in the background, so time the PC was asleep, hibernating or switched off is read from the Windows
System event log at the end and left out. If that log can't be read, `end` says so and counts the whole time.
A single run can't be longer than 7 days: `end` refuses and asks you to correct the start in `current.json` (or delete
the file to discard the task).

While the TaskTracker window is open, `start`, `end` and today's report are refused, so only one copy times tasks.
When you open the window, it carries on with the task started with `start`.
The report shows the running task as running and doesn't include it yet: its time is added when it ends.

To see a report without starting the tracker, run `TaskTracker report` for today, or `TaskTracker report 2026-10-23`
for any other day (`yyyy-MM-dd`). It prints the summary and exits. Today's report refuses while TaskTracker is running,
because that copy may still be timing a task; press `R` in that window instead. Other days can be reported at any time.

Run `TaskTracker --help` (or `-h`, `-?`, `/?`) for a summary of commands and keys.

Task names are any text. Matching ignores case and surrounding spaces, so `Bug fix` and `bug fix ` count as the same task.
The task picker lists tasks from the last 7 days, with the most recently used first.

## Logs

Logs go to one file per day: `%USERPROFILE%\.TaskTracker\logs\yyyy-MM-dd.json`

```json
{
  "date": "2026-10-24",
  "tasks": [
    {
      "task": "Fix login bug",
      "totalSeconds": 4627,
      "intervals": [
        { "start": "2026-10-24T09:00:05+02:00", "end": "2026-10-24T09:47:12+02:00", "durationSeconds": 2827 }
      ]
    }
  ]
}
```

- **Saving:** the running interval is saved as soon as it starts and again every minute. Ctrl+C
  and closing the window also save it. If the app is killed, you lose at most about one minute.
- **Sleep:** if the PC sleeps for more than 5 minutes while a task is running, the sleep time is not counted.
- **Midnight:** an interval that runs past 00:00 is split between the two days' files.
- **One instance:** a second copy of the app refuses to start, so two copies can't overwrite each other's log.

## Code signing (optional)

Signing is **not required**. The **publish (single exe)** task produces an unsigned
`TaskTracker.exe` that runs normally. A self-signed signature changes little for others:
Windows still shows "Unknown publisher" and SmartScreen still warns about downloaded files.
Its only uses are proving the exe hasn't been modified, and showing a verified publisher on
your own PCs if you trust the certificate there.

No certificate is included in the repository. To sign your own builds:

1. **Create a certificate** (once): run `scripts\New-SigningCert.ps1`. It creates a certificate
   named `CN=Vladimir Kozlov (TaskTracker code signing)`, valid for 3 years, in your personal certificate store.
   It also exports the public part to `signing\tasktracker-codesign.cer`. The `signing/` folder is
   git-ignored, and the private key never leaves the PC.
2. **Sign:** use the **publish + sign** task instead of **publish (single exe)**. It runs
   `scripts\Sign-Exe.ps1`, which adds a timestamp, so the signature stays valid after the certificate expires.
   This step needs internet access.
3. **Trust the certificate on a PC** (optional, once per PC, no admin needed): copy the `.cer` file into
   that PC's `signing\` folder, then run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\Trust-SigningCert.ps1
   ```

   Windows asks for confirmation. Click **Yes** only if the thumbprint in the dialog matches the one the script printed.

To check an exe: right-click it → **Properties → Digital Signatures**, or run
`Get-AuthenticodeSignature TaskTracker.exe`.

## Tests

```powershell
dotnet test
```

## License

[MIT](LICENSE)
