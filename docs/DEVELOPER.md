# Developer guide

## Layout

```
src/Proxmark3Gui/
  Core/                 no UI; everything testable in isolation
    ProcessRunner.cs    runs an external exe, streams output, timeout + cancel
    WslManager.cs       detect/install WSL, import the distro, run bash in it
    UsbIpdManager.cs    install usbipd-win, list devices, bind/attach/detach
    Downloader.cs       streamed HTTP download with progress
    Elevation.cs        admin check + relaunch
    AppSettings.cs      JSON settings in %APPDATA%\Proxmark3Gui
    SetupManager.cs     the whole install as a list of idempotent, resumable steps
    Pm3Runner.cs        runs pm3 client commands inside the distro, file import/export
    Pm3Parse.cs         regex parsers for pm3 output (UID, keys, dump names, ...)
  UI/
    MainForm.cs             shell, log, busy/cancel plumbing, widget helpers
    MainForm.Setup.cs       setup checklist tab
    MainForm.Device.cs      detect/connect tab
    MainForm.Clone.cs       HF and LF clone tabs
    MainForm.Files.cs       backup/restore tab
    MainForm.Console.cs     raw pm3 + raw shell tab
    MainForm.Settings.cs    settings + maintenance tab
  Program.cs            entry point, global crash handler
  app.manifest          requireAdministrator, PerMonitorV2 DPI
legacy/                 the original single-form prototype, kept for reference
docs/                   user and developer docs
scripts/                mirror-sync helpers
.github/workflows/      CI build + release
```

## Design rules

- **Core never touches WinForms.** The UI passes an `Action<string>` for log lines and a
  `CancellationToken`; Core knows nothing else about the UI.
- **Every setup step is idempotent and has a `Check`.** Re-running setup never redoes finished
  work, and the app can resume after a crash or reboot. Completed step ids are persisted in
  settings.
- **The managed distro runs as root**, so there is no sudo password anywhere.
- **Long operations stream.** `ProcessRunner` raises a line callback as output arrives, so the
  build log moves in real time and the UI thread never blocks.

## Build and run

```bash
dotnet build src/Proxmark3Gui/Proxmark3Gui.csproj -c Debug
dotnet run   --project src/Proxmark3Gui/Proxmark3Gui.csproj      # needs admin for USB/WSL steps
dotnet publish src/Proxmark3Gui/Proxmark3Gui.csproj -c Release -o publish
```

Release publish is single-file, self-contained, win-x64 — one `.exe`, no runtime needed.

## Adding a pm3 action

1. Add a parser to `Pm3Parse.cs` if you need a new value out of the output, with the sample text
   in a comment.
2. Add an `async Task` method on the relevant `MainForm.*.cs` partial that calls
   `WithBusyAsync(...)` and `Pm3.RunAsync(...)`.
3. Wire a button with the `Btn(...)` helper. Buttons are disabled while busy automatically; mark a
   Stop button with `Tag = "always-on"` to keep it live.

## Adding a setup step

Add a `SetupStep` in `SetupManager.BuildSteps()` with an `Id`, a `Check` that returns true when
already satisfied, and a `Run` that returns `StepResult.Success/Fail/Reboot`. The checklist tab and
the unattended runner pick it up with no further changes.

## Version number

The version lives in **one place**: the plain-text `VERSION` file at the repo root (currently
`1.0.0`). Edit that file by hand — nothing else.

- The build reads it via MSBuild and stamps it into `Version`, `AssemblyVersion`, `FileVersion`
  and `InformationalVersion`, and prints `building version X.Y.Z` in the build output.
- The app reads it back from the assembly and shows it in the title bar (`... vX.Y.Z`), the header
  badge and the first log line (`MainForm.VersionString`).

So: change `VERSION`, rebuild, and the new number appears in the window title, the exe properties
and the build log. If `VERSION` is ever missing, the build falls back to `1.0.0`.

## App icon

`src/Proxmark3Gui/app.ico` is wired in through `<ApplicationIcon>`, so it is the exe icon; the form
reuses it at runtime with `Icon.ExtractAssociatedIcon`. Replace that one file to rebrand.

## Releasing

Push a tag like `v2.0.0`. The `release` workflow publishes the single-file exe and attaches it to a
GitHub release. See `.github/workflows/`.
