# Rewake (beta)

*Your workspace, back the way you left it.*

Rewake remembers what you have open — applications, terminals (with their
folders), and File Explorer windows — and brings it all back after a power cut,
restart, or accidental shutdown. It sits in the system tray and snapshots your
workspace every 2 minutes.

**Version:** 1.0.0-beta.1 · **Requires:** Windows 11 (64-bit), nothing else to install

---

## Install (testers)

1. Copy `Rewake.exe` to a folder that won't move (e.g. `C:\Tools\Rewake\`).
   Avoid running it from Downloads if you plan to enable "Run at startup" —
   the startup entry points at wherever the exe was when you ticked the box.
2. Double-click `Rewake.exe`.
3. **Windows SmartScreen will warn you** ("Windows protected your PC") because
   the app isn't code-signed yet. Click **More info → Run anyway**.
4. Some antivirus tools may flag it: Rewake reads other programs' data
   (window titles, command lines, terminal working folders) to do its job,
   which can look suspicious to an AV. Allow it if you're comfortable.

## Quick start

- The window opens with your **capture history** on the left. Captures happen
  automatically every 2 minutes; press the **camera** button to capture now.
- Pick a capture, tick the apps/windows/folders you want back, and press
  **Restore Workspace**. Apps launch one at a time (so the PC doesn't choke),
  each window returning to its saved position — a progress card shows each
  app's status live.
- **Closing the window does not exit** — Rewake keeps capturing from the
  system tray. Right-click the tray icon → **Exit** to quit for real.
- After a power cut, restore the pinned **Last Session · BEFORE SHUTDOWN**
  entry at the top of the history — that's everything you had open.
- Tick **Run at startup** so Rewake is already running after a reboot.

## Good to know

- **Terminals reopen in their last folder** (Windows Terminal, PowerShell,
  cmd, WSL…) — multi-tab windows come back with one tab per saved path.
- **Browsers** reopen and restore their own tabs — enable your browser's
  "Continue where you left off" setting for full effect.
- **Force relaunch**: normally an app is skipped when one of its windows is
  already open, and a folder is skipped when Explorer is already showing it;
  tick this to open them anyway.

## Known limitations (V1)

- Terminals running **as administrator** reopen at the default folder — their
  path can't be read from an elevated process.
- Some Microsoft Store apps can't be relaunched and are skipped (the Activity
  Log will say so).
- Unsaved document content can't be recovered — Rewake restores windows, not
  file contents. Enable each app's own recovery (browser session restore,
  editor auto-save) for the rest.
- Windows are restored to their saved monitor; if that monitor is no longer
  attached, the window is placed on the nearest screen instead.

## Your data & privacy

Everything stays on your machine — Rewake makes no network connections.
Data lives in `%LOCALAPPDATA%\Rewake`:

| File | Contents |
| --- | --- |
| `snapshots\` | the last 30 captures (window titles, exe paths, command lines, folder paths) |
| `last-session.json` | the pinned pre-reboot capture |
| `app-settings.json` | your preferences |
| `crash.log` | written only if something goes wrong |

## Reporting a problem

Please include:

1. What you did and what you expected to happen.
2. The version — hover the tray icon (e.g. "Rewake 1.0.0-beta.1").
3. `%LOCALAPPDATA%\Rewake\crash.log`, if it exists.
4. A screenshot of the Activity Log at the bottom of the window, if relevant.

## Uninstall

1. Untick **Run at startup** in the app.
2. Right-click the tray icon → **Exit**.
3. Delete `Rewake.exe` and the `%LOCALAPPDATA%\Rewake` folder.

---

## Developer notes

### Architecture (MVVM)

```
Rewake.csproj               net9.0-windows, WPF (+ WinForms for the tray icon)
App.xaml / App.xaml.cs      composition root: settings, tray icon, capture
                            timer, single-instance guard, crash logging
Models\                     Snapshot/AppEntry/WindowInfo JSON models,
                            AppSettings, RestoreStatus
Views\MainWindow.xaml       pure XAML bindings; code-behind has view-only
                            concerns (log auto-scroll, title-bar theme, tray)
ViewModels\                 MainViewModel (commands + state), item VMs,
                            RelayCommand, ViewModelBase
Services\                   SnapshotService (capture/persist), RestoreService
                            (staggered relaunch + progress), SettingsService,
                            ThemeService, IconCache, Paths, NativeMethods
Themes\                     Dark.xaml / Light.xaml swappable dictionaries
Assets\                     icon (.ico, 8 sizes) + SVG sources + build-icon.ps1
```

### Feature highlights

- EnumWindows-based capture: every visible window of every app is its own
  entry; excluded system processes and cloaked/tool windows are filtered.
- Terminal working directories are read from each shell process's PEB and
  restored via `wt -w new -d … ; new-tab -d …`.
- First capture after a new boot preserves the newest pre-reboot snapshot as
  the pinned "Last Session".
- Restore launches sequentially (0.7 s gap, 2 s after heavy apps), waits up to
  4 s per window, repositions it (clamped to attached monitors), and reports
  per-item progress to the overlay UI.

### Build & publish

Requires the .NET 9 SDK. Tester build (self-contained, no runtime needed):

```powershell
dotnet publish Rewake.csproj -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true -o publish
```

Dev build (smaller, needs .NET 9 Desktop Runtime installed):

```powershell
dotnet publish Rewake.csproj -c Release -r win-x64 --self-contained false `
    /p:PublishSingleFile=true -o publish
```
