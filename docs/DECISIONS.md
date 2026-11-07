# Decisions and their reasons

Written at the end of the first build session (6–7 November 2026) so the
"why" survives. Newest at the bottom.

## Why a fork of Rewake, and not Workset or OpenWorkspace

Three existing tools were examined before writing code.

- **OpenWorkspace** (commercial, Open Workspace LLC) was reverse-engineered
  with REA from its signed installer: a .NET Framework 4.7.2 WPF app with C++
  hook DLLs injected into other processes, `ReadProcessMemory` /
  `WriteProcessMemory`, UI Automation and COM automation for Office, a
  browser extension with a native messaging host, a licence server and Sentry
  telemetry. Powerful, but invasive and fragile against app updates; its
  ideas were taken (an adapter class per app, "attention" messages, display
  profiles), not its approach.
- **Workset** (Tauri 2 + Rust + React) has the best architecture on paper —
  UI never touches the OS, normalized coordinates, a scored window matcher, a
  launch pipeline with per-item status — but no licence, was generated in a
  day, and has real bugs: Store app windows filtered out
  (`ApplicationFrameWindow`), window drift from `GetWindowRect` vs
  `SetWindowPos`, `cmd /C start` despite "never a shell", a `safeInvoke` that
  fabricates results on backend errors, and two vocabularies across the
  TS/Rust boundary. Its design was borrowed; no code.
- **Rewake** (C# / .NET 9 / WPF, MIT) is small, honest and used by a human.
  Its capture of command lines, terminal working directories (via the PEB),
  Explorer folders, Store execution aliases and browser-argument sanitizing
  are exactly the "senses" Restage needed. It had no workspace model,
  thumbnails, desktops or profiles, and the same drift bug; those were built
  on top of it.

## Why C# and not Rust (or a Windhawk mod)

The user prefers Rust in general. For this app the deciding facts were: every
feature is Win32/COM, where C# interop is the least work; WPF gives the
thumbnail gallery and can host live DWM thumbnails later (a WebView cannot);
Rewake exists under MIT; and the 2026 consensus online is C# (WPF/WinUI 3)
for conventional Windows apps, with Rust strong via Tauri and
`windows-reactor` still in preview. A Windhawk mod was ruled out: mods have
no UI of their own, live inside Explorer, and nothing here needs Windows
itself patched.

## Virtual desktops: public API only

Windows has no public API to switch desktops. Libraries like `winvd` use
internal COM interfaces whose GUIDs change per build (the dev machine runs
build 26300, above winvd's tested range). Decision: the user switches
(prompt with Task View button, or "never switch"), Restage polls the
registry's `CurrentVirtualDesktop`, moves its own window along with the
public `IVirtualDesktopManager::MoveWindowToDesktop`, and applies the
wallpaper afterwards. Cost: windows of other apps cannot be moved between
desktops.

## Placement without drift

`GetWindowRect` includes the invisible resize border; `DwmGetWindowAttribute(
DWMWA_EXTENDED_FRAME_BOUNDS)` is what the user sees. Capture the latter,
measure the delta on the live window when placing, and compensate. Minimized
and maximized windows go through `WINDOWPLACEMENT`, which is symmetric.
Verified: capture → restore in place → capture again gives identical
rectangles for every window.

## Model

One JSON file per workspace, no database (Workset's SQLite was overkill).
`WorkspaceItem` = Kind + Exe + Args + Target (identity) + one placement per
display profile + Enabled + LaunchPolicy + OpenContent. Adapters, not the
restore service, know what a Target means.

## Things removed from Rewake, on purpose

Auto-capture every 2 minutes, the capture history, "Last session before
shutdown", Stay Awake, capture names/favourites. Restage is about workspaces
you name; a rolling history would flood the gallery.

## Visual choices made with the user

Name: "Restage" (briefly "Workspace Switcher"). Accent from the Windows accent
colour, darker for button fills, lighter for marks and text. Icon: first
Rewake's monitor-with-windows artwork recoloured to indigo `#4B3DFF` after
several rounds (violet and purple read as lilac on the user's screen); then,
at the user's request for something more suitable, a plain indigo tile with
a white three-pane layout, which reads at 16 px and matches the layout
glyph the app uses for workspaces. Also drawn and set aside: a tile with a
return arrow (reads as "refresh"), stacked cards (bland) and three windows
on a stage floor (a bar chart at small sizes). Later that night the user
brought their own designed logo, an isometric indigo frame with a mint
card, as a finished package (`logo/`); it replaced the tile everywhere and
the SVG-to-ICO build script went with it. Theme follows Windows by default,
with a toggle switch to pin light or dark. The desktop prompt has two modes
only (ask / never); "wait silently" was dropped.

## Obsidian note restore

Obsidian did not reliably reopen the captured note on its own, so the note
is read from the window title, located in the vault by file name, and opened
through `obsidian://open?vault=…&file=…` — also when the vault is already
open (`AppAdapter.ReuseAsync`). The per-item `OpenContent` checkbox ("note",
"folder", "arguments") lets a user ask for a bare start instead; it was
removed once on request and reinstated the same evening.

## Chrome tabs: profile only, for now (7 November 2026)

The wish was to record a Chrome window's tabs and let the per-item checkbox
decide whether a restore opens them. Chrome writes its windows and tabs to
`<profile>\Sessions\Session_<timestamp>` (SNSS version 3: a 16-bit record
size, an 8-bit command id, a payload; command 255 marks the end of the
initial state). A probe of the previous run's file parsed cleanly, but Chrome
opens the *current* file with exclusive access while it runs, so every read
during a capture fails with a sharing violation. That lock is deliberate and
is not worked around. Decision with the user: Chrome stays as it is (the
profile is relaunched and Chrome restores its own session); a companion
extension talking to Restage through the browsers' native messaging API, the
supported way to see a running browser's tabs, may follow later. Reading only
the address bar through UI Automation was rejected: it yields the active tab
alone and would duplicate what Chrome restores itself.

## Repository

GitHub `roosmsg/Restage`: first a fork of Rewake named `Restage`, renamed
`Restage-Windows`, then (7 November, late) duplicated into a fresh
repository under the original name so GitHub no longer shows it as a fork
of Rewake; the full history came along, the licence is still MIT and the
attribution stays in `NOTICE.md`. Description
ending in "Not a window manager.", topics workspace / workspaces /
virtual-desktop / productivity. Rewake's tags were deleted from the fork;
Restage has no release yet. `LICENSE` is the untouched MIT text (GitHub
only detects it that way); the Rewake attribution lives in `NOTICE.md`.
