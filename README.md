# TaskTracker

A Windows console app that tracks how much time you spend on tasks. Type a task name, and a
timer starts. You can switch between tasks, pause, and see today's totals at any time.

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
