# FocalContextual — what we know, and how we could build our own

Notes gathered while reverse-engineering OpenWorkspace 2.8.2040.41547 (the
commercial app Restage was measured against) and reading its public pages.
Kept so a separate "focal/contextual" app can be designed later without
repeating the research. Facts are labelled **public**, **observed** (taken
from the binary with REA) or **inferred** (our reading of those facts).

## 1. The idea (public)

OpenWorkspace calls it *FocalContextual™*, "a spatial model for focus and
context", and is explicit that it is **not a window-tiling scheme**: tiling
fits windows to the screen, FocalContextual gives the primary content a
stable place.

- "One or more **Focal Panels** define the **Focal Region**, even when a
  panel is empty."
- "The **Contextual Region** is the desktop surface outside those
  boundaries." Supporting windows stay visible there while the focal content
  changes.
- Four stages: **Establish** (define a focal panel) → **Focus** ("snap the
  content currently requiring attention into a focal panel") → **Context**
  (keep supporting windows visible around it) → **Shift** (move content into
  or out of focus as attention moves; "the panel and surrounding workspace
  remain in place").
- "It can be used on a laptop display or across multiple monitors" and is
  "most useful when there is enough space" to keep primary and supporting
  content visible together.
- The public pages say nothing about hotkeys, dimming, blur or sound for
  this mode; those come from the binary (below).

Sources: openworkspace.com/desktop-model (#focalcontextual),
/how-it-works, /framework.

## 2. What the binary shows (observed)

Class and setting names from `OpenWorkspace.exe` (.NET Framework 4.7.2,
WPF), grouped by what they are for:

**Panels and regions**
- `PanelCanvas`, `PanelView`, `LivePanelView`, `IconView`, `PanelPlaceholder`,
  `PanelTrayItem`, `WorkspaceCurrentPanelViewModel`, `PanelViewViewModel`
- `ScreenArea`, `ContainerDefinition`, `IWindowContainer`, `WindowDefinition`,
  `DesktopGeometryManager`, `DesktopLayout`, `GeomData`, `Monitors`,
  `DisplayProfileManager`
- `TooltipFocalRegionInstance`, `WindowTooLargeEventArgs`, `HitResult`

**Moving windows in and out of panels**
- `IWindowDragger`, `LiveWindowDragger`, `ThumbnailDragger`,
  `PanelWindowDragger`, `DragDropManager`, `MouseTrail`, `MouseTrailAdorner`,
  `PreviewMouseDownBehavior`, `InputEvents`, `MouseEventHandler`
- P/Invokes: `SetWindowPlacement`, `SetWindowPos`, `MoveWindow`,
  `ShowWindow`, `SetWindowRgn`/`GetWindowRgn`, `PrintWindow`, `BitBlt`,
  `SetForegroundWindow`, `AttachThreadInput`, `BringWindowToTop`,
  `SendInput`, `GetAsyncKeyState`

**Live previews**
- Namespace `Aero`: `DWMApi`, `DWM_THUMBNAIL_PROPERTIES`, `PSIZE`
  (= `DwmRegisterThumbnail` / `DwmUpdateThumbnailProperties`)
- `BrowserThumbnailBinding`, `ThumbnailRestoreSession`,
  `ThumbnailImageSnapshot`, `BrowserThumbnailTarget`

**Dimming and blur of the contextual region**
- `BlurBackgroundWindow` (Views.Windows), `WindowBlur`,
  `Native.AccentPolicy` / `WindowCompositionAttributeData` /
  `WindowCompositionAttribute` / `AccentState`
  (= `SetWindowCompositionAttribute` acrylic/blur-behind)
- `ExplorerBlurSafetyPolicy`, `ExplorerSystemBackdropManager`,
  `AppDialogBackdropManager`, `DesktopDialogBackdropWindow`,
  `WorkspaceOverlayWindow`, asset `DimmingOverlayLogo.png`
- Log lines: "ExplorerBlur: Blur background shown.", "ExplorerBlur:
  BlockDesktopWhilePreparingToShow starting.", "ExplorerBlur:
  ApplyDesktopBlurSetting applied."
- Feature flag `EnableExplorerTitleBarBlurToggle`

**Following the user's focus**
- `WinEventHook`, `IWindowMonitor` / `WindowMonitor`,
  `WindowMonitorEventArgs`, `WindowMonitorWindowClosedEventArgs`,
  `EnumerateOpenedWindows`, `WindowFinder`
- P/Invokes: `SetWinEventHook`, `UnhookWinEvent`, `EnumWindows`,
  `GetForegroundWindow`, `GetWindowPlacement`, `AccessibleObjectFromWindow`
- Hook DLLs (`HookDll64.dll` etc., `SetWindowsHookEx` with `WH_CBT`,
  `WH_CALLWNDPROC`, `WH_GETMESSAGE`, `WH_SHELL`, `WH_KEYBOARD_LL`,
  `WH_MOUSE_LL` wrappers in namespace `System.Hooks`): OpenWorkspace injects
  into other processes to see window creation, moves and messages early.

**Settings keys**
- `EnableFocalContextual`, `EnableFocalContextualHotkey`,
  `FocalContextualIntroSeen`, `FocalContextualIntroThresholdCrossed`,
  `FocalContextualIntroConfirmationSeen`, `ContextualVolume`
- Onboarding: `FocalContextualIntroDialog`,
  `FocalContextualOnboardingContent`,
  `FocalContextualConfirmationCoachMark`; log lines "Showing Focal
  Contextual intro confirmation coach mark." and "…suppressed because
  display was already reserved or confirmation was seen."

**Sound**
- `SystemAudioManager`, `SoundEffect`, `NAudioSoundPlayer` (NAudio.Wasapi /
  WinMM), setting `ContextualVolume`

**Desktop environment per workspace** (adjacent, also used by this mode)
- `WindowManagementSettings` (Windows Snap on/off via
  `SPI_SETWINARRANGING`), `SystemTaskbar` / `TaskbarButtonLabelsSettings`
  (taskbar labels via `TaskbarGlomLevel`), `WorkspaceDesktopSettings`
  (taskbar auto-hide via `SHAppBarMessage`, monitor brightness via WMI and
  DDC/CI), `DesktopIconToggleDiagnostics` (show/hide desktop icons),
  `DesktopWallpaperManager`, `WorkspaceTheme` / `WindowsColorMode`

## 3. How it most likely works (inferred)

1. The user defines one or more **panel rectangles** on the desktop
   (`ScreenArea` / `ContainerDefinition`), per display profile. A panel is a
   reserved area that exists even when empty ("even when a panel is empty").
2. The **Focal Region** = union of the panels. A window is "snapped into" a
   panel by dragging it there (`PanelWindowDragger`, `HitResult`) or via a
   hotkey (`EnableFocalContextualHotkey`); the app then sets its placement to
   the panel rectangle (`SetWindowPlacement`/`SetWindowPos`). Windows larger
   than the panel raise `WindowTooLarge`.
3. Panels can show a **live preview** of what is in them (DWM thumbnails,
   `LivePanelView`) and small icons of their windows (`IconView`,
   `PanelTrayItem`), so the panel doubles as a dock for the things you cycle
   through.
4. The **Contextual Region** is everything else. Supporting windows stay
   where they are; a **dim/blur overlay** (`BlurBackgroundWindow` with
   acrylic via `SetWindowCompositionAttribute`, `DimmingOverlayLogo.png`) can
   lower them visually, and `ContextualVolume` probably lowers the audio of
   apps that are not focal (per-app volume), so attention is pulled to the
   focal panel without hiding context.
5. A **WinEvent hook** on foreground changes keeps the focal panel's content
   in sync with what the user activates, and the injected hook DLLs let it
   react to window creation and moves before they are painted (that part is
   what we would *not* copy).
6. An **onboarding** flow shows a coach mark the first time a threshold is
   crossed (likely: enough windows open / first drag into a panel).
7. Everything is stored with the workspace, so restoring a workspace also
   restores its panels; Windows Snap can be switched off while a workspace
   is active so Windows' own snapping does not fight the panels.

## 4. Building our own, with public APIs only

Everything above is reachable without hooks or memory access; Restage
already has half of it in `Restage.Core`.

| Need | Public API / existing piece |
| --- | --- |
| Monitors, DPI, profiles | `DisplayProfiles` (Restage) |
| Enumerate windows, titles, exe, AUMID | `WindowEnumerator` (Restage) |
| Snap a window into a panel rectangle, drift-free | `WindowPlacer.Apply` (Restage, DWM frame bounds + border compensation) |
| Follow focus / moves / closes | `SetWinEventHook` with `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_LOCATIONCHANGE`, `EVENT_OBJECT_DESTROY` (out-of-context, no DLL injection) |
| Live preview of a window inside a panel | `DwmRegisterThumbnail` / `DwmUpdateThumbnailProperties` into our own HWND (WPF via `HwndHost`) |
| Dim the contextual region | One click-through overlay window per monitor: `WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`, semi-transparent fill with a hole over the focal panels (geometry, not `SetWindowRgn`), kept below the focal windows with `SetWindowPos` z-order |
| Blur instead of dim | `SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)` — works on 10/11 but is **undocumented**; or Windows 11's documented `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` on our own overlay |
| Reserve the panel so other windows avoid it | `SHAppBarMessage` (app bar) if the panel is at a screen edge; otherwise overlays only |
| Keep a panel's window on top while focal | `SetWindowPos(HWND_TOP…)`; use `HWND_TOPMOST` sparingly |
| Hotkey "snap active window into focal panel" | `RegisterHotKey` (or Restage's future global-shortcut layer) |
| Contextual volume | `IAudioSessionManager2` → `ISimpleAudioVolume` per process (Core Audio, documented); NAudio wraps it |
| Panel geometry per workspace | extend `Workspace` with `Panels: List<ScreenRect + display device>` |
| Drag a window into a panel | low-level mouse hook `WH_MOUSE_LL` in our own process (allowed, no injection) + `WindowFromPoint`, or simply drop targets on the overlay when a window is dragged over it |

### Suggested shape

- A separate app (the user wants it separate from Restage) that can read
  Restage's workspace files for panel definitions, or its own JSON.
- `FocalSession`: panels + the HWND each panel currently holds + the set of
  contextual windows; one per display profile.
- `FocusTracker`: WinEvent hook → "window X became foreground" → if X is in a
  panel, nothing; if the user pressed the hotkey, snap X into the active
  panel and (optionally) send the previous occupant to the contextual
  region at its old placement.
- `Overlay`: one WPF window per monitor, click-through, draws dim/blur with
  holes for the panels; hidden while no panel has content.
- `Volume`: when enabled, set non-focal sessions to `ContextualVolume`
  (e.g. 30 %) and restore them when they become focal or the mode ends.
- Onboarding: one coach mark the first time a window is dragged near a
  panel; a setting to switch the whole mode off (OpenWorkspace has
  `EnableFocalContextual` for that).

### Risks and limits

- Overlays and full-screen apps/games: hide the overlay when a full-screen
  window is foreground (`SHQueryUserNotificationState` or a size check).
- Acrylic via `SetWindowCompositionAttribute` is undocumented and has cost
  on large areas; dim (alpha) is cheap and always works.
- DWM thumbnails cannot be captured or scaled beyond the source size and
  do not render minimized windows.
- Elevated windows cannot be moved; Explorer's desktop itself cannot be
  blurred without touching Explorer (OpenWorkspace's `ExplorerBlur`
  apparently does; skip it).
- Windows Snap, FancyZones (PowerToys) and this mode fight over the same
  windows; offer to disable Snap while a focal session is active
  (`SystemParametersInfo(SPI_SETWINARRANGING)`, documented).

## 5. What to reuse from Restage when the time comes

`Win32`, `WindowEnumerator`, `WindowPlacer`, `DisplayProfiles`,
`VirtualDesktops`, the theme/accent service, the tray + single-instance
shell, and the adapters (to know *what* a focal window is, not just its
HWND). Keep the two apps separate but let them share `Restage.Core` as a
project reference.
