# Restage

*Save a desktop. Come back to it with one click.*

Restage saves what you have open — VS Code folders, Obsidian vaults, Firefox
and Chrome profiles and web apps, Explorer folders, terminals, any other app —
together with the Windows 11 virtual desktop it lives on, its wallpaper and
the position of every window. A workspace is a card with a screenshot in a
gallery; **Restore** brings it all back, on every monitor arrangement you have
saved a layout for.

Restage is a fork of [Rewake](https://github.com/Anurag-patil-26/Rewake-Windows)
by Anurag Patil (MIT), which it keeps as its foundation: the capture of
command lines, terminal working directories and Explorer folders, the WPF
shell and the design language all come from there.

**Version:** 0.1.0 · **Requires:** Windows 11 (64-bit) · **Stack:** C# / .NET 10, WPF

---

## What a workspace brings back

| Item | Saved as | Restored by |
| --- | --- | --- |
| VS Code window | the folder or `.code-workspace` it shows | `code -n <folder>`; VS Code reopens the editors itself |
| Obsidian window | the vault and the note it shows | `obsidian://open?vault=…&file=…`, also when the vault is already open |
| Firefox window | the profile | starting that profile once; Firefox restores windows and tabs |
| Firefox web app on the taskbar | its exact launch arguments | the same arguments |
| Chrome / Edge / Brave window | the profile | starting that profile once; the browser restores its tabs |
| Chrome / Edge / Brave web app | profile + app id | `--profile-directory=… --app-id=…` |
| Explorer window | the folder path | `explorer.exe <path>` |
| Terminal | the shells' working directories | `wt -d … ; new-tab -d …` |
| Anything else | exe path + cleaned-up arguments | starting it (Store apps through their execution alias) |

Plus, per workspace: the virtual desktop (by id, then by name), the wallpaper,
and one window layout per **display profile** (your monitor arrangement). Dock
the laptop, press *Update*, and the workspace knows both layouts.

The principle: Restage stores *identity and placement* and lets every
application restore its own session. No hooks, no reading other processes'
memory, no browser extension.

## Quick start

1. Arrange a desktop the way you work.
2. Open Restage and press **New workspace**. The window hides, the desktop is
   photographed, and you see the windows that were found. Untick what you do
   not want, give it a name, **Save workspace**.
3. Later: press **Restore** on the card. Apps open one at a time; windows that
   are already open are reused and moved into place.

Closing the window keeps Restage in the system tray; the tray menu lists the
workspaces too. Right-click the tray icon → **Exit** to quit for real.

### Virtual desktops

Windows has no public API to switch virtual desktops, so Restage never does it
behind your back. If a workspace lives on another desktop, a prompt names it
and offers Task View; press <kbd>Win</kbd>+<kbd>Ctrl</kbd>+<kbd>←</kbd>/<kbd>→</kbd>
and the restore continues the moment you arrive. The gear menu can turn this
off (restore on the current desktop).

### Items and policies

Select a card to see its items. Each can be ticked off, and its label cycles
through how it comes back: **reuse if open** (default) · **always launch** ·
**only if open** · **skip**. *Always relaunch* in the gear menu overrides the
first for one session.

Items with content of their own (an Obsidian note, a VS Code or Explorer
folder, a terminal's folders, a file passed as an argument) have a second
checkbox — *note*, *folder*, *arguments* — that is on by default. Untick it
and the app just starts, without that note, folder or file.

## Command line

`restage.exe` is installed beside the app:

```text
restage capture <name>              save the desktop as a new workspace
restage update <name|id>            re-capture an existing workspace (keeps its settings)
restage list                        list workspaces
restage show <name|id>              print a workspace's items
restage restore <name|id> [--force] [--here]
restage delete <id>
restage inspect                     print what a capture would record, without saving
```

The app itself answers `Restage.exe --restore <name>` and the URI
`restage://restore/<name>`, both of which reach the running instance. That is
the hook for hotkey tools and voice assistants such as
[Hex](https://github.com/anomalyco/hex): a spoken command that opens
`restage://restore/Werk` restores the workspace named *Werk*.

## Your data and privacy

Everything stays on your machine. Restage makes no network connections.
Data lives in `%LOCALAPPDATA%\Restage`:

| Path | Contents |
| --- | --- |
| `workspaces\<id>\workspace.json` | the workspace: items, placements, desktop, wallpaper |
| `workspaces\<id>\thumbnail.png` | the screenshot shown on its card |
| `app-settings.json` | preferences |
| `crash.log` | written only if something goes wrong |

Workspaces are plain JSON: back them up, diff them, edit them by hand.

## Known limitations

- Windows of **elevated** (administrator) apps cannot be read or moved.
- **Explorer tabs** come back as separate windows.
- **Chrome web apps** opened after the browser started are recognised by their
  AppUserModelID; a browser that does not set one is treated as a plain window.
- Only the **current** virtual desktop's windows are captured.
- Antivirus tools may flag Restage: it reads window titles, command lines and
  terminal working folders to do its job.

## Building

Requires the .NET 10 SDK. The repository pins nuget.org in `nuget.config`.

```powershell
dotnet build Restage.slnx -c Release
dotnet test  tests/Restage.Core.Tests/Restage.Core.Tests.csproj -c Release
```

Self-contained publish (no runtime needed on the target machine):

```powershell
dotnet publish src/Restage.App/Restage.App.csproj -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish
dotnet publish src/Restage.Cli/Restage.Cli.csproj -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true -o publish
```

`installer\Restage.iss` builds a per-user Inno Setup installer from `publish\`.

### Layout

```text
src/Restage.Core   models, capture and restore, Win32/COM interop — no UI
  Adapters/        one per kind of app: VS Code, Obsidian, Firefox, Chromium, Explorer, Generic
  Native/          Win32, VirtualDesktops (public API + registry), DesktopWallpaper
  Services/        WindowEnumerator, WindowPlacer, DisplayProfiles, Thumbnails,
                   WorkspaceStore, WorkspaceCaptureService, WorkspaceRestoreService
src/Restage.App    WPF: gallery, details pane, overlays, tray, settings
src/Restage.Cli    restage.exe
tests/             xUnit tests for the pure parts (parsers, matching, merging, store)
```

Two details worth knowing: window rectangles are captured from the DWM
extended frame bounds and restored with border compensation, so a saved
window lands on the same pixels instead of drifting; and the process is
per-monitor DPI aware, so every coordinate is a physical pixel on every
monitor.

## Third-party

- [Rewake](https://github.com/Anurag-patil-26/Rewake-Windows) by Anurag Patil — MIT. Restage's foundation; see `LICENSE`.
- [Inter](https://github.com/rsms/inter) by Rasmus Andersson — SIL Open Font License 1.1, bundled in `src/Restage.App/Assets/Fonts`.
- [Lucide](https://lucide.dev) icon paths — ISC License.

## License

MIT. See `LICENSE`.
