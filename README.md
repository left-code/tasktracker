# TaskTracker

A Windows console app that tracks how much time you spend on tasks. Type a task name, and a
timer starts. You can switch between tasks, pause, and see today's totals at any time.

## Run

```powershell
dotnet run --project src/TaskTracker
```

In VS Code, press **F5**. The app runs in the integrated terminal, because it needs key presses.

To build a standalone `TaskTracker.exe`, run the **publish (single exe)** task. The exe is written to `publish/`.

## Keys

| Key | Action |
|-----|--------|
| `P` | Pause / resume the current task |
| `S` | Switch task: pick a recent task (type to search) or choose **+ New task...** |
| `N` | Type a new task name straight away |
| `T` | Today's summary. In the summary, `I` lists every interval |
| `Q` | Stop, save, and print today's summary |

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

## Code signing

`TaskTracker.exe` is signed with a **self-signed** certificate. The signature proves the exe
hasn't been modified since it was signed. Windows only shows the publisher as verified on
PCs that trust the certificate. Everywhere else it still shows "Unknown publisher".

| | |
|---|---|
| Subject | `CN=vlkozl (TaskTracker code signing)` |
| Thumbprint | `38B82DA0BFC53C70417BA7F12AC0449403E808CC` |
| Valid until | 2029-10-24 (signatures are timestamped, so they stay valid after that) |
| Public certificate | [`signing/tasktracker-codesign.cer`](signing/tasktracker-codesign.cer) |

**Trust it on a PC** (once per PC, no admin needed):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Trust-SigningCert.ps1
```

Windows asks for confirmation. Before you click **Yes**, check that the thumbprint in the dialog matches the one above.
To check an exe: right-click it → **Properties → Digital Signatures**, or run
`Get-AuthenticodeSignature TaskTracker.exe`.

**Signing (maintainer):** run `scripts\New-SigningCert.ps1` once. It creates the certificate
in your personal certificate store and exports the public `.cer`. After that, run the **publish + sign**
task, which runs `scripts\Sign-Exe.ps1`. The private key never leaves that PC, and
`*.pfx` files are git-ignored.

## Tests

```powershell
dotnet test
```
