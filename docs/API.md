# Restage local API

The running Restage listens on `127.0.0.1` so tools on the same PC can drive
it: voice assistants, hotkey tools, scripts, a future Hex plugin. It is plain
HTTP with JSON, loopback only, and needs a token. Restage never opens a port
on the network.

## Finding it

While the app runs, `%LOCALAPPDATA%\Restage\local-api.json` holds:

```json
{
  "apiVersion": 1,
  "port": 51234,
  "token": "9f3c…",
  "pid": 12345
}
```

The port is random and the token new for every run; the file is deleted at
exit. Check that `pid` is alive before trusting a file (a crash leaves it
behind). Send the token on every request:

```text
Authorization: Bearer 9f3c…
```

A request without it gets `401`. A request carrying an `Origin` header (a
web page) gets `403`: browsers are not a client of this API.

`restage api` prints the file and the status; `restage command "<text>"`
sends a command; `restage restore <name>` goes through the API when the app
is up.

## Routes

Every reply is JSON. Errors look like `{"error": "<code>", "message": "…"}`.

### `GET /status`

```json
{ "app": "Restage", "version": "0.1.0", "apiVersion": 1, "pid": 12345,
  "busy": false, "capturing": false, "restoring": null, "workspaces": 3 }
```

`restoring` holds the workspace being started while one is.

### `GET /workspaces`

```json
{ "workspaces": [
  { "id": "afstuderen-f7bd80", "name": "Afstuderen", "desktop": "Research",
    "items": 6, "hotkey": "Ctrl+Alt+F",
    "updatedAt": "2026-11-07T14:35:12+01:00", "lastOpenedAt": "2026-11-07T15:02:40+01:00" }
] }
```

### `POST /restore`

Starts a workspace by exact name (case does not matter) or id. Answers at
once with `202`; the start runs on in the app, which shows the progress.
When a start is already running it is cancelled in favour of this one, like
pressing one hotkey after another.

```json
{ "workspace": "Afstuderen", "front": true, "force": false, "here": false }
```

- `front` (default true): after placing, raise the workspace's windows, the
  first item on top. See the note on focus below.
- `force`: relaunch apps that are already open.
- `here`: skip the virtual-desktop switch.

Replies: `202 {"started": {…workspace…}}`, `404 not-found`,
`409 busy` (a capture is in progress), `400 missing-workspace`.

### `POST /capture`

Saves everything open right now as a new workspace, without the review step
the window offers. Waits for the capture (a second or two).

```json
{ "name": "Thesis" }
```

Replies: `200 {"saved": {"id", "name", "items"}}`, `409 busy`,
`400 missing-name`.

### `POST /command`

What was said, as said. Restage finds the verb and the name, in English or
Dutch, and matches the name with tolerance for how speech comes back.

```json
{ "text": "Open afstuderen, please." }
```

Understood:

- start: `open …`, `start …`, `switch to …`, `go to …`, `show …`, `restore
  …`, `ga naar …`, `schakel naar …`, `wissel naar …`, `herstel …`, `toon
  …`; a bare name works too. Words such as "the", "my", "workspace",
  "please", "de", "werkruimte", "alsjeblieft" and a leading "hey Restage"
  are ignored.
- capture: `save (this) as …`, `capture (this) as …`, `remember (this) as …`,
  `sla (dit) op als …`, `bewaar (dit) als …`, `onthoud (dit) als …`. The
  name is taken as said, casing included.

Matching: exact beats a name contained in the phrase, beats a phrase that is
the start of a name, beats an edit-distance similarity (so "af studeren"
and "afstudeeren" still find *Afstuderen*). A hit needs a score of 60 and a
lead of 8 over the runner-up; otherwise nothing starts.

Replies: `202 {"started": {…}, "heard": "…"}` for a start,
`200 {"saved": {…}, "heard": "…"}` for a capture,
`404 no-match` and `409 ambiguous` with `"candidates": [{id, name, score}]`,
`400 empty` / `missing-name`.

## Examples

PowerShell:

```powershell
$api = Get-Content "$env:LOCALAPPDATA\Restage\local-api.json" | ConvertFrom-Json
$h = @{ Authorization = "Bearer $($api.token)" }
Invoke-RestMethod "http://127.0.0.1:$($api.port)/workspaces" -Headers $h
Invoke-RestMethod "http://127.0.0.1:$($api.port)/command" -Method Post -Headers $h `
  -ContentType 'application/json' -Body '{"text":"open afstuderen"}'
```

curl:

```sh
curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:$PORT/status
curl -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
     -d '{"workspace":"Afstuderen"}' http://127.0.0.1:$PORT/restore
```

## For voice tools

Send the transcript to `POST /command` and show the reply's `message` when
the status is not 2xx; Restage does the parsing. The app does not need its
window open: it answers from the tray.

A note on focus: Windows lets only the process that received the last
input, or the foreground process, activate a window. A start from the API
places the workspace's windows and puts them on top, but the activation
may stay with your tool's window. A tool that is the foreground process can
hand the right over before calling, with
`AllowSetForegroundWindow(<pid from local-api.json>)`.

## Versioning

`apiVersion` in the file and in `/status` is 1. Fields are only added
within a version; a route or a field that goes away means a new version.
