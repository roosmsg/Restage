<p align="center"><img src="logo/Restage-transparent-1024.png" width="160" alt="Restage logo"></p>

# Restage

*Workspace manager. Save your active applications as a workspace. Come back to them with one (voice) command.*

Restage saves the applications you have open — VS Code folders, Obsidian
vaults, Firefox and Chrome profiles and web apps, Explorer folders, terminals,
any other app — together with the position of every window and, if you use
Windows 11 virtual desktops, the virtual desktop they live on and its
wallpaper. A workspace is a card with a screenshot in a
gallery; **Start** brings it all back, on every monitor arrangement you have
saved a layout for.

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

[PowerToys Workspaces](https://learn.microsoft.com/windows/powertoys/workspaces)
is the obvious comparison, and for many people it is all they need: it
remembers which apps were open and where their windows were, then launches
them and puts them back. Restage does the same, with the same public Windows
APIs, and goes one step further: it also remembers what each window had open
and brings that back too.

## Quick start

1. Open and arrange the applications the way you work.
2. Open Restage and press **New workspace**. The window hides, a screenshot is
   taken for the card, and you see the applications that were found. Untick
   what you do not want, give it a name, **Save workspace**.
3. Later: press **Start** on the card. Apps open one at a time; windows that
   are already open are reused and moved into place.

Closing the window keeps Restage in the system tray; the tray menu lists the
workspaces too. Right-click the tray icon → **Exit** to quit for real.
While a workspace's apps are being launched, the tray icon carries a small
loading ring. Settings can make the tray icon monochrome, white or black to
match the taskbar, or hide it. Hidden, Restage keeps running (hotkeys, local API);
start it again from the Start menu to open the window.

### Virtual desktops

Windows has no public API to switch virtual desktops, so Restage never does it
behind your back. If a workspace lives on another virtual desktop, a prompt names it
and offers Task View; press <kbd>Win</kbd>+<kbd>Ctrl</kbd>+<kbd>←</kbd>/<kbd>→</kbd>
and the workspace starts the moment you arrive. The settings panel can turn
this off (start on the current virtual desktop).

### Items and policies

Select a card to see its items. Each can be ticked off, and its label switches
how it comes back: **reuse if open** (default) or **always launch**.

Items with content of their own (an Obsidian note, a VS Code or Explorer
folder, a terminal's folders, a file passed as an argument) have a second
checkbox — *note*, *folder*, *folders*, *file* — that is on by default. Untick
it and the app just starts, without that note, folder or file. Apps whose
arguments are mere switches get no such checkbox.

## Command line

`restage.exe` is installed beside the app:

```text
restage capture <name>              save the active applications as a new workspace
restage update <name|id>            re-capture an existing workspace (keeps its settings)
restage list                        list workspaces
restage show <name|id>              print a workspace's items
restage restore <name|id> [--force] [--here]
restage delete <id>
restage inspect                     print what a capture would record, without saving
restage command <text>              hand what was said to the running app ("open research")
restage api                         where the running app's local API is
```

The app itself answers `Restage.exe --restore <name>` and the URI
`restage://restore/<name>`, both of which reach the running instance.

## Keyboard shortcuts

Each workspace can have a global shortcut (the pencil in its details pane):
pressing it anywhere in Windows starts the workspace, or brings its windows
back to their places and to the front when they are already open. One more
shortcut opens Restage itself, and puts it away again when it is in front:
Settings › *Shortcut to open Restage*. Handy with the tray icon hidden. Nothing is bound until you set it.

## Connecting other apps: the local API

Other apps on the same PC can drive Restage: a voice tool such as
[Hex](https://anoma.ly/hex) or [Handy](https://handy.computer), a Stream
Deck button, a launcher, a script. Restage offers them a small local API:
while it runs, it listens on `127.0.0.1` with a token. Settings ›
*Connect apps* copies the command URL and the token to give such an app;
both stay the same when Restage restarts (they are also announced in
`%LOCALAPPDATA%\Restage\local-api.json`). The API lists, starts and
captures workspaces, and `POST /command` takes a plain sentence ("open
research", "switch to thesis", "save this as Thesis"), in English or Dutch,
and works out the rest, with some tolerance for names that are slightly
off, such as a voice tool's transcription. Restage itself never listens to
the microphone; a voice tool sends what it heard. [docs/API.md](docs/API.md)
has every route and examples; `restage command "<text>"` tries one from a
shell.

## Your data and privacy

Everything stays on your machine. Restage makes no network connections; its
local API listens on the loopback address only, with a token that only your
own processes can read. Data lives in `%LOCALAPPDATA%\Restage`:

| Path | Contents |
| --- | --- |
| `workspaces\<id>\workspace.json` | the workspace: items, placements, virtual desktop, wallpaper |
| `workspaces\<id>\thumbnail.png` | the screenshot shown on its card |
| `app-settings.json` | preferences |
| `local-api.json` | where the running app's local API is (port, token); gone when it exits |
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

- [Rewake](https://github.com/Anurag-patil-26/Rewake-Windows) by Anurag Patil — MIT. Restage's foundation; see `NOTICE.md`.
- [Inter](https://github.com/rsms/inter) by Rasmus Andersson — SIL Open Font License 1.1, bundled in `src/Restage.App/Assets/Fonts`.
- [Lucide](https://lucide.dev) icon paths — ISC License.

## License

MIT. See `LICENSE`.
