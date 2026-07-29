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
- The title bar holds a **sidebar toggle** on the left (hide the history for a
  wider capture list) and a **gear** on the right, next to the window buttons.
  The gear has your preferences — auto capture, run at startup, force
  relaunch — plus the theme switch and **Open data folder**.
- Tick **Run at startup** under the gear so Rewake is already running after a
  reboot.

## Good to know

- **Terminals reopen in their last folder** (Windows Terminal, PowerShell,
  cmd, WSL…) — multi-tab windows come back with one tab per saved path.
- **Browsers** reopen and restore their own tabs — enable your browser's
  "Continue where you left off" setting for full effect.
- **Force relaunch** (under the gear): normally an app is skipped when one of
  its windows is already open, and a folder is skipped when Explorer is already
  showing it; tick this to open them anyway.

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

## Third-party

- **[Inter](https://github.com/rsms/inter)** by Rasmus Andersson — SIL Open Font
  License 1.1. Bundled in `Assets\Fonts\`; full licence in `Inter-OFL.txt`.
- **[Lucide](https://lucide.dev)** icon paths — ISC License.

## Uninstall

1. Untick **Run at startup** under the app's gear icon.
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
Assets\Fonts\               Inter (Regular/Medium/SemiBold) + its OFL licence
```

### UI conventions

- **Icons are Lucide geometry, not an icon font.** Each is a `Geometry` resource
  in App.xaml, converted from the official SVG at
  [lucide-icons/lucide](https://github.com/lucide-icons/lucide/tree/main/icons)
  and kept in Lucide's 24x24 box. Buttons hand one to their template via `Tag`,
  and a `Viewbox` scales the whole box so every icon keeps its intended optical
  size. One stroke weight throughout, and nothing extra to ship.
  When adding one, note that Lucide splits an icon across several `<path>`
  elements: concatenating them into one geometry turns a leading relative `m`
  into a move relative to the *previous* subpath's endpoint, so it has to be
  rewritten absolute first.
- **Inter is vendored**, not assumed installed. Static weights rather than the
  variable TTF — WPF renders only a variable font's default instance, so Medium
  and SemiBold would silently come out Regular.
- One type scale (`TitleText` / `BodyText` / `SubtleText` / `SectionLabel`), each
  `BasedOn` the implicit `TextBlock` style. That `BasedOn` is load-bearing: an
  explicit `Style` replaces the implicit one outright, so a heading that skips it
  loses `Foreground` and renders black on a black background.
- **Square corners throughout** — buttons, cards, list rows, badges, checkboxes,
  menus. Only the scrollbar thumbs stay rounded.
- The **activity log sits inside the content column**, not across the window, so
  the capture history runs the full height and the log starts at the seam.
- **Accent green means one thing: the action.** Restore Workspace is the only
  filled accent button; capture and delete are transparent with an accent
  outline, and their icon turns accent on hover.

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
