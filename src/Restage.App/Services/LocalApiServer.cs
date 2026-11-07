using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;
using Restage.Services.Api;
using Restage.ViewModels;

namespace Restage.Services;

/// <summary>
/// The local API: HTTP on 127.0.0.1 at a random port, announced with a
/// per-run bearer token in %LOCALAPPDATA%\Restage\local-api.json, so voice
/// and automation tools on this PC can list, start and capture workspaces,
/// or hand over what was said as text (POST /command). Loopback only; a
/// request without the token, or from a web page (an Origin header), is
/// refused. Every route is described in docs/API.md.
/// </summary>
public sealed class LocalApiServer : IDisposable
{
    public const int Version = 1;
    const int MaxBody = 64 * 1024;
    // Relaxed escaping: the replies are read by tools and people, never put
    // into HTML, so "Ctrl+Alt+F" need not come out as "Ctrl\u002BAlt\u002BF".
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly MainViewModel _vm;
    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    HttpListener? _listener;

    public int Port { get; private set; }

    public LocalApiServer(MainViewModel vm) => _vm = vm;

    /// <summary>Takes a free port, listens, and writes the discovery file; false (and logged) when no port could be had.</summary>
    public bool Start()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var port = RandomNumberGenerator.GetInt32(49152, 65535);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { listener.Start(); }
            catch (HttpListenerException) { listener.Close(); continue; }
            _listener = listener;
            Port = port;
            LocalApiFile.Write(new LocalApiInfo(Version, port, _token, Environment.ProcessId));
            _ = LoopAsync(listener);
            return true;
        }
        _vm.Log("Local API: no free port found; it is off for this run.");
        return false;
    }

    async Task LoopAsync(HttpListener listener)
    {
        while (true)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }
            _ = HandleAsync(context);
        }
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        int status;
        object payload;
        try
        {
            if (request.Headers["Origin"] != null)
                (status, payload) = (403, Error("browser-origin", "Requests from web pages are refused."));
            else if (!Authorized(request))
                (status, payload) = (401, Error("unauthorized", "Send the token from local-api.json as a bearer token."));
            else if (request.ContentLength64 > MaxBody)
                (status, payload) = (413, Error("too-large", $"Bodies are at most {MaxBody} bytes."));
            else
            {
                var body = await ReadBodyAsync(request);
                var path = request.Url?.AbsolutePath ?? "/";
                // State lives on the UI thread; so does every command.
                (status, payload) = await _dispatcher.InvokeAsync(() => RouteAsync(request.HttpMethod, path, body)).Task.Unwrap();
            }
        }
        catch (Exception ex)
        {
            (status, payload) = (500, Error("internal", ex.Message));
        }
        await WriteAsync(context.Response, status, payload);
    }

    bool Authorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(header[7..].Trim());
        var wanted = Encoding.UTF8.GetBytes(_token);
        return given.Length == wanted.Length && CryptographicOperations.FixedTimeEquals(given, wanted);
    }

    static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody) return "";
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var buffer = new char[MaxBody + 1];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        return read > MaxBody ? throw new InvalidDataException("body too large") : new string(buffer, 0, read);
    }

    static async Task WriteAsync(HttpListenerResponse response, int status, object payload)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            response.StatusCode = status;
            response.ContentType = "application/json; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
        }
        catch { }
        finally { try { response.Close(); } catch { } }
    }

    // ---- Routes (UI thread) ----------------------------------------------------

    sealed record RestoreRequest(string? Workspace, bool? Front, bool? Force, bool? Here);
    sealed record CaptureRequest(string? Name);
    sealed record CommandRequest(string? Text);

    async Task<(int, object)> RouteAsync(string method, string path, string body)
    {
        path = path.TrimEnd('/').ToLowerInvariant();
        if (path.Length == 0) path = "/status";
        switch (method.ToUpperInvariant(), path)
        {
            case ("GET", "/status"):
                return (200, Status());
            case ("GET", "/workspaces"):
                return (200, new { workspaces = _vm.Workspaces.Select(Describe).ToList() });
            case ("POST", "/restore"):
            {
                if (Parse<RestoreRequest>(body) is not { } r) return BadJson();
                return StartWorkspace(r.Workspace, r.Front ?? true, r.Force ?? false, r.Here ?? false, heard: null);
            }
            case ("POST", "/capture"):
            {
                if (Parse<CaptureRequest>(body) is not { } r) return BadJson();
                return await CaptureAsync(r.Name, heard: null);
            }
            case ("POST", "/command"):
            {
                if (Parse<CommandRequest>(body) is not { } r) return BadJson();
                return await CommandAsync(r.Text);
            }
            case (_, "/status" or "/workspaces" or "/restore" or "/capture" or "/command"):
                return (405, Error("method-not-allowed", $"{path} does not take {method}."));
            default:
                return (404, Error("unknown-route", $"No {path} here; the routes are in docs/API.md."));
        }
    }

    static T? Parse<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonSerializer.Deserialize<T>(body, Json); }
        catch (JsonException) { return null; }
    }

    static (int, object) BadJson() => (400, Error("bad-request", "The body must be a JSON object; see docs/API.md."));

    object Status() => new
    {
        app = "Restage",
        version = App.AppVersion,
        apiVersion = Version,
        pid = Environment.ProcessId,
        busy = _vm.IsBusy,
        capturing = _vm.IsCapturing,
        restoring = _vm.IsRestoring && _vm.Selected is { } card ? Describe(card) : null,
        workspaces = _vm.Workspaces.Count,
    };

    object Describe(WorkspaceCardViewModel card) => new
    {
        id = card.Id,
        name = card.Name,
        desktop = card.Model.Desktop?.DisplayName,
        items = card.Model.Items.Count,
        hotkey = _vm.HotkeyTextFor(card.Id),
        updatedAt = card.Model.UpdatedAt,
        lastOpenedAt = card.Model.LastOpenedAt,
    };

    /// <summary>Starts a workspace by exact name (case-insensitive) or id; answers at once, the start runs on.</summary>
    (int, object) StartWorkspace(string? which, bool front, bool force, bool here, string? heard)
    {
        if (string.IsNullOrWhiteSpace(which)) return (400, Error("missing-workspace", "Give \"workspace\": a name or an id."));
        which = which.Trim();
        var card = _vm.Workspaces.FirstOrDefault(w => w.Name.Equals(which, StringComparison.OrdinalIgnoreCase))
                   ?? _vm.Workspaces.FirstOrDefault(w => w.Id == which);
        if (card == null) return (404, Error("not-found", $"No workspace named \"{which}\"."));
        if (_vm.IsCapturing) return (409, Error("busy", "A capture is in progress; try again in a moment."));
        _vm.Log(heard == null ? $"API: start \"{card.Name}\"." : $"API: \"{heard}\" → start \"{card.Name}\".");
        _ = _vm.StartByCommandAsync(card, front, force, here);
        return (202, new { started = Describe(card), heard });
    }

    async Task<(int, object)> CaptureAsync(string? name, string? heard)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) return (400, Error("missing-name", "Give \"name\" for the new workspace."));
        if (_vm.IsBusy) return (409, Error("busy", "A capture or a start is in progress; try again in a moment."));
        _vm.Log(heard == null ? $"API: capture as \"{name}\"." : $"API: \"{heard}\" → capture as \"{name}\".");
        var saved = await _vm.CaptureByCommandAsync(name);
        return saved == null
            ? (409, Error("busy", "Another capture got in first."))
            : (200, new { saved = new { id = saved.Id, name = saved.Name, items = saved.Items.Count }, heard });
    }

    /// <summary>What was said, as said: parsed for a verb and a name, the name matched with tolerance.</summary>
    async Task<(int, object)> CommandAsync(string? text)
    {
        if (CommandParser.Parse(text ?? "") is not { } command)
            return (400, Error("empty", "Give \"text\": what was said."));
        if (command.Kind == CommandParser.Kind.Capture)
        {
            if (command.Name.Length == 0) return (400, Error("missing-name", "Say what to call it: \"save as <name>\"."));
            return await CaptureAsync(command.Name, text);
        }
        var names = _vm.Workspaces.Select(w => (w.Id, w.Name)).ToList();
        var best = NameMatcher.Best(command.Name, names);
        if (best != null) return StartWorkspace(best.Id, front: true, force: false, here: false, heard: text);

        var ranked = NameMatcher.Rank(command.Name, names);
        var candidates = ranked.Take(3).Select(c => new { id = c.Id, name = c.Name, score = c.Score }).ToList();
        var ambiguous = ranked.Count > 1 && ranked[0].Score >= NameMatcher.MinScore;
        _vm.Log(ambiguous
            ? $"API: \"{text}\" could be {ranked[0].Name} or {ranked[1].Name}; nothing started."
            : $"API: \"{text}\" names no workspace.");
        return ambiguous
            ? (409, Error("ambiguous", $"\"{command.Name}\" could be {ranked[0].Name} or {ranked[1].Name}.", new { text, candidates }))
            : (404, Error("no-match", $"No workspace sounds like \"{command.Name}\".", new { text, candidates }));
    }

    static Dictionary<string, object?> Error(string code, string message, object? extra = null)
    {
        var payload = new Dictionary<string, object?> { ["error"] = code, ["message"] = message };
        if (extra != null)
            foreach (var property in extra.GetType().GetProperties())
                payload[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = property.GetValue(extra);
        return payload;
    }

    public void Dispose()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
        LocalApiFile.Delete();
    }
}
