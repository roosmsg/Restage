# Restage — working notes for Claude Code

Read this first. It records what was learned while building Restage so a new
session can continue without re-deriving it. `docs/DECISIONS.md` holds the
reasoning behind the bigger choices.

## What Restage is

A Windows 11 app that saves the desktop you are working in as a *workspace*
(editors with their folders, Obsidian vaults with the open note, browser
profiles and web apps, Explorer folders, terminals, other apps, the virtual
desktop and its wallpaper, window placement per monitor arrangement) and
brings it back with one click, a hotkey or a voice command (through the
local API). Fork of Rewake (MIT, see `NOTICE.md`). Not a
window manager; no telemetry, no animations, no sounds.

Repo: `roosmsg/Restage` (a plain copy of the earlier fork
`roosmsg/Restage-Windows`, no longer flagged as a fork; remote `windows`
still points at the old one). Integration branch `local-trunk`; `main` is
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
  - VS Code: folder resolved from the title against
    `%APPDATA%\Code\User\workspaceStorage\*\workspace.json` (most recently
    written `state.vscdb` wins): a " - " segment equal to the folder name
    (default title format) scores above the name as a whole word anywhere
    (custom `window.title` such as `${rootName} ${focusedView}
    ${activeEditorLanguageId}`, which the user has). `storage.json`'s
    `openedWindows` is empty while VS Code runs, so it is useless live.
    Launch through the CLI next to the exe, `bin\code.cmd -n "<folder>"`,
    hidden via `cmd.exe /c`: since VS Code 1.145 (Code.exe at the install
    root, the app in a per-commit folder) a direct `Code.exe -n "<folder>"`
    does nothing while an instance runs (verified: no window in 30 s; the
    CLI shows one in under a second).
  - Obsidian: title `Note - Vault - Obsidian 1.x`; vault path from
    `%APPDATA%\obsidian\obsidian.json`; note file found by name in the vault.
    Launch `Obsidian.exe "obsidian://open?vault=…&file=…"`; the same URI is
    sent via `ReuseAsync` when the vault is already open.
  - Firefox: a web app ("taskbar tab") window carries the app's GUID as its
    AppUserModelID (its shortcut has the same id); the app's name, start URL
    and container come from `<profile>\taskbartabs\taskbartabs.json`, its icon
    from `taskbartabs\icons\<id>.ico`, and it is relaunched with the
    shortcut's own arguments (`-taskbar-tab <id> -new-window <url> -profile
    <path> -container <n>`). Do not trust the process command line: all
    windows of a profile share one process, so a Firefox started by a web
    app shortcut carries that app's arguments on every browser window, and
    an app opened while Firefox ran carries none. A `-profile <path>` equal
    to the default profile (profiles.ini) counts as the default profile.
    Browser windows: one launch per profile, Firefox restores tabs itself.
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
  `AccentHoverBrush` = 52 %. Icon: the user's own logo (an isometric indigo
  frame with a mint card), delivered as a finished package in `logo/`
  (`Restage.ico` with 10 PNG frames 16–256 px, PNGs, an editable SVG
  redraw, a transparent 1024 px master, a dark "GitHub background"
  version). `src/Restage.App/Assets/Restage.ico`, `Restage-256.png` and
  `Restage.svg` are copies of those; there is no icon build script any
  more. The title bar picks the ICO frame that is 1:1 at the window's DPI.
- **Tray icon:** `AppSettings.ShowTrayIcon` (hidden: the app keeps running;
  a second launch opens the window) and `MonochromeTrayIcon`: `TrayGlyph`
  draws the SVG's frame and card as thin outlines in one colour (1 px at
  16 px; the frame is cut back 40 units around the card) into an in-memory PNG .ico, white or black by the taskbar's mode
  (`SystemUsesLightTheme`, not the apps' `AppsUseLightTheme`), redrawn on
  `UserPreferenceChanged`. While `IsRestoring`, `TrayGlyph.WithBusyBadge`
  adds a still loading ring (no animation, by rule) bottom right, on either
  icon, and the tooltip names the workspace.
- **Single instance / hooks:** mutex `Local\Restage.SingleInstance`; a second
  launch writes `restore-request.txt` and signals the first. `--restore <name>`
  and the `restage://restore/<name>` URI (registered in HKCU at every start)
  both route there — this is the hook for voice tools (Hex, Handy) and
  hotkey tools.
- **Global hotkeys:** one per workspace, plus `app:open`
  (`HotkeyActions.OpenApp`, shows the window; set from the settings panel in
  the same edit panel, `EditingApp` hides the name). `AppSettings.Hotkeys` maps
  `workspace:<id>` (`HotkeyActions`) to text such as `Ctrl+Alt+R` (`Models/Hotkey.cs` parses and prints it; Ctrl, Alt or Win is
  required). `HotkeyService` registers them with `RegisterHotKey` on a
  message-only `HwndSource`, so they work with the window closed. The details
  pane shows it; the pencil opens an edit panel (name + shortcut, saved
  together). Its box records while it has keyboard focus and all hotkeys are
  unregistered meanwhile, or an already bound combination would fire instead
  of being recorded; a trial `RegisterHotKey` then tells whether another app
  or the shell holds the combination ("Taken by another app or by Windows"). Empty by default. A hotkey start
  passes `RestoreOptions.BringToFront`: after placing, the workspace's
  windows are raised (first item on top and activated; WM_HOTKEY gives the
  process the right to take the foreground) — that is "switching" to an
  open workspace. A start still running is cancelled by the next hotkey.
- **Local API:** `LocalApiServer` (App) is an `HttpListener` on
  `http://127.0.0.1:<port>/` (loopback prefixes need no URL ACL); port and
  bearer token are picked on the first run and kept (`AppSettings.ApiPort`,
  `ApiToken`) so a voice tool's setup survives restarts; a taken port gets a
  random one for that run only (logged). Settings › Connect apps opens a panel that
  shows the URL and copies URL and token. Discovery file `%LOCALAPPDATA%\Restage\local-api.json`
  (`LocalApiFile`, Core; readers check the pid). Routes in `docs/API.md`:
  status, workspaces, restore, capture, command. Requests are handled on the
  UI thread (`Dispatcher.InvokeAsync`), a restore answers 202 at once. The
  text route is `CommandParser` (verb + name, EN/NL, keeps casing) then
  `NameMatcher` (normalise, score exact > contains > prefix > edit distance;
  a win needs ≥ 60 and a lead of 8), both pure and tested. `restage.exe`
  is a client (`LocalApiClient`): `restore` goes through the app when it
  runs, `command` and `api` need it. Voice: Restage has no dictation of its
  own, by decision (see DECISIONS.md); a voice tool sends what it heard to
  `POST /command`.
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
- VS Code windows are tied to their folder through the window title only
  (folder name or path as a " - " segment or a whole word, see Adapters).
  The user wants more than that: a `window.title` without `${rootName}` or
  `${rootPath}` defeats it, and the format differs per VS Code profile.
  Public alternatives checked on 2026-11-07: `code --status` lists every
  open window as `Window (title)` plus `Folder (name)` whatever the title
  format, but gives only the folder *name* (still resolved via
  workspaceStorage by name and recency) and scans the folder's files
  first (seconds). The launch IPC behind it (`getMainProcessInfo`) carries
  the folder URIs but is private and version-bound. Renderer and extension
  host command lines, environments and `storage.json` carry nothing usable
  live. The reliable route is a small companion extension that writes
  window (title, pid) → folder path to `%LOCALAPPDATA%\Restage`, with
  `--status` as a fallback when the title yields nothing.
- Only the current virtual desktop's windows are captured.
- Elevated apps cannot be read or moved.
- Rewake's auto-capture history, "Last session before shutdown" and Stay
  Awake were removed on purpose; "Last session" could return as an automatic
  workspace if wanted.
- Not yet: named display profiles in the UI, a GitHub release
  with binaries, a Hex plugin (or any voice tool's side) that calls the
  local API; `AllowSetForegroundWindow` from the caller is what would let a
  started workspace take the focus (docs/API.md).
- A separate "focal/contextual" app is on the wish list; everything learned
  about OpenWorkspace's FocalContextual mode (public description, what the
  binary reveals, how to build it with public APIs on top of `Restage.Core`)
  is in `docs/FOCAL-CONTEXTUAL.md`.
