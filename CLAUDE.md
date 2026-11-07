# Restage — working notes for Claude Code

Read this first. It records what was learned while building Restage so a new
session can continue without re-deriving it. `docs/DECISIONS.md` holds the
reasoning behind the bigger choices.

## What Restage is

A Windows 11 app that saves the desktop you are working in as a *workspace*
(editors with their folders, Obsidian vaults with the open note, browser
profiles and web apps, Explorer folders, terminals, other apps, the virtual
desktop and its wallpaper, window placement per monitor arrangement) and
brings it back with one click. Fork of Rewake (MIT, see `NOTICE.md`). Not a
window manager; no telemetry, no animations, no sounds.

Repo: `roosmsg/Restage-Windows`. Integration branch `local-trunk`; `main` is
pushed to the same commit after every change (`git push origin local-trunk &&
git push origin local-trunk:main`).

## Build, test, run

- .NET 10 SDK. On the dev machine it is installed per user, **not on PATH**:
  `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`. Use that path explicitly.
- The user-level NuGet.Config has no sources; the repo's `nuget.config` pins
  nuget.org. Do not remove it.
- Build: `dotnet build Restage.slnx -c Release`
- Test: `dotnet test tests/Restage.Core.Tests/Restage.Core.Tests.csproj -c Release`
  (xUnit; pure parts only: parsers, matching, merge, store, profiles)
- Run: `src/Restage.App/bin/Release/net10.0-windows/Restage.exe` (stop a
  running instance first — `taskkill /IM Restage.exe /F` — or the build cannot
  overwrite `Restage.Core.dll`).
- CLI: `src/Restage.Cli/bin/Release/net10.0-windows/restage.exe`
  (`inspect` prints what a capture would record without saving — the fastest
  way to check an adapter against the live desktop).
- Publish and installer: see README "Building"; `installer/Restage.iss` is
  Inno Setup 6, per-user, own AppId.

## Conventions

- Commits: Commitizen style (`type(scope): subject` + a body explaining the
  thought process; `git cz` is not installed, write them by hand). One
  behaviour per commit. End with the Co-Authored-By line the session provides.
- When a test fails, do not commit; `dotnet test | grep` hides the exit code
  unless `set -o pipefail` is on.
- Language of code, comments, commits, README: English. The user writes Dutch
  in chat; answer in the language they use.
- No animations, sounds or telemetry, ever. Instant state changes are fine.

## Layout

```
src/Restage.Core      no UI; models, adapters, capture/restore, Win32/COM
  Models/             Workspace, WorkspaceItem, WindowPlacement, DisplayProfile, AppSettings
  Adapters/           AppAdapter base + VSCode, Obsidian, Firefox, Chromium, Explorer, Generic
  Native/             Win32 (P/Invoke, AUMID, PEB reads), VirtualDesktops, DesktopWallpaper
  Services/           WindowEnumerator, WindowPlacer, DisplayProfiles, Thumbnails,
                      WorkspaceStore, WorkspaceCaptureService, WorkspaceRestoreService, LaunchHelpers
src/Restage.App       WPF (MVVM, hand-rolled RelayCommand/ViewModelBase from Rewake)
src/Restage.Cli       restage.exe
tests/                xUnit
```

Data: `%LOCALAPPDATA%\Restage\workspaces\<id>\workspace.json` + `thumbnail.png`,
`app-settings.json`, `crash.log`. Plain JSON on purpose (no database).

## How the pieces work (the non-obvious parts)

- **Principle:** store *identity + placement*; let each app restore its own
  session. No hooks into other processes, no memory writes, no browser
  extension. Command lines and terminal cwds are read from the PEB (fast;
  WMI only as fallback in full captures).
- **Adapters** (`AdapterRegistry.All`, first `CanDescribe` wins, Generic last):
  - VS Code: folder resolved from a title segment against
    `%APPDATA%\Code\User\workspaceStorage\*\workspace.json` (most recently
    written `state.vscdb` wins). `storage.json`'s `openedWindows` is empty
    while VS Code runs, so it is useless live. Launch `Code.exe -n "<folder>"`.
  - Obsidian: title `Note - Vault - Obsidian 1.x`; vault path from
    `%APPDATA%\obsidian\obsidian.json`; note file found by name in the vault.
    Launch `Obsidian.exe "obsidian://open?vault=…&file=…"`; the same URI is
    sent via `ReuseAsync` when the vault is already open.
  - Firefox: taskbar web apps are their own `firefox.exe -taskbar-tab <id>
    -new-window <url> -profile <path>`; relaunched verbatim. Browser windows:
    one launch per profile, Firefox restores tabs itself.
  - Chromium: web apps vs browser windows told apart by the window's
    AppUserModelID (`Chrome._crx_<app id>[.<profile>]`), not the shared
    command line. Chrome caps the id at 64 characters and cuts each component
    in the middle (head + tail), so a web app in the default profile shows 26
    of its 32 letters; the full id is found by rebuilding the id of every app
    under `<profile>\Web Applications\_crx_*` and comparing
    (`ChromiumAdapter.BuildAumid`). `restage inspect --windows` prints the raw
    ids. Never turn an unknown id component into `--profile-directory`:
    Chrome creates that profile.
  - Explorer: folders + HWNDs from `Shell.Application` (STA thread), matched
    by path; Explorer windows are excluded from the generic enumeration.
  - Generic: Store apps via `%LOCALAPPDATA%\Microsoft\WindowsApps\<exe>`
    alias (Spotify from the Store works this way); `--start-in-tray` and
    similar flags are dropped (Signal).
- **Placement:** capture with `DWMWA_EXTENDED_FRAME_BOUNDS`, apply with
  `SetWindowPos` plus the measured invisible-border delta; minimized and
  maximized windows round-trip through `WINDOWPLACEMENT`. Verified pixel-exact
  on the dev machine. The process is per-monitor DPI aware v2 (`app.manifest`
  for WPF, `Win32.EnsurePerMonitorDpi()` for Core/CLI).
- **Virtual desktops:** public API only (`IVirtualDesktopManager` + the
  registry keys under `HKCU\…\Explorer\VirtualDesktops`). The user switches;
  the app polls `CurrentVirtualDesktop` and moves its own window along. The
  `winvd`-style internal COM interfaces were deliberately avoided (they break
  per Windows build; this machine runs build 26300, above what winvd tests).
- **Wallpaper:** `IDesktopWallpaper`, applied after the desktop step so it
  lands on the right desktop.
- **Display profiles:** id = hash of device names, geometry and DPI; each item
  keeps one placement per profile id; re-capture merges other profiles'
  placements, policies and flags by (Kind, Target|Title).
- **Theme:** `ThemeService` follows Windows light/dark (`AppsUseLightTheme`)
  when the setting is "System", and paints accents from the Windows accent
  colour (`DWM\AccentColor`): `AccentBrush` = accent mixed 18 % toward white
  (marks, text, progress), `AccentFillBrush` = 62 % (button fills),
  `AccentHoverBrush` = 52 %. Icon colour: indigo `#4B3DFF`, regenerate with
  `src/Restage.App/Assets/build-icon.ps1` (needs Chrome).
- **Single instance / hooks:** mutex `Local\Restage.SingleInstance`; a second
  launch writes `restore-request.txt` and signals the first. `--restore <name>`
  and the `restage://restore/<name>` URI (registered in HKCU at every start)
  both route there — this is the hook for Hex / hotkey tools.
- **WindowChrome gotcha:** the `WM_GETMINMAXINFO` hook in `MainWindow` must
  restate `MinTrackSize`, or WPF's MinWidth/MinHeight are not enforced.

## Testing the UI without disturbing the user

Use `PrintWindow(hwnd, hdc, 2)` from PowerShell for screenshots and UI
Automation (`InvokePattern`) for clicks. **Never** `AppActivate` /
`SetForegroundWindow` the app while the user may be typing: it happened once
and their keystrokes landed in Restage (theme flipped, overlay closed).
Popups (settings menu) are separate top-level HWNDs of the process; enumerate
them with `EnumWindows` filtered by PID.

## Known gaps / ideas not done

- Explorer tabs come back as separate windows (no public API).
- Browser tabs are the browser's own session restore; a profile per workspace
  is the intended way to get per-workspace tabs.
- Chrome/Edge tabs cannot be read from disk during a capture: the browser
  holds `<profile>\Sessions\Session_<timestamp>` exclusively while it runs
  (the previous run's file is readable: SNSS v3, 16-bit size + 8-bit command
  id per record, marker command 255). Decided with the user to leave it;
  if wanted later, the supported route is a companion extension plus a
  native messaging host. See `docs/DECISIONS.md`.
- Only the current virtual desktop's windows are captured.
- Elevated apps cannot be read or moved.
- Rewake's auto-capture history, "Last session before shutdown" and Stay
  Awake were removed on purpose; "Last session" could return as an automatic
  workspace if wanted.
- Not yet: global hotkeys, named display profiles in the UI, a GitHub release
  with binaries, Hex voice config.
- A separate "focal/contextual" app is on the wish list; everything learned
  about OpenWorkspace's FocalContextual mode (public description, what the
  binary reveals, how to build it with public APIs on top of `Restage.Core`)
  is in `docs/FOCAL-CONTEXTUAL.md`.
