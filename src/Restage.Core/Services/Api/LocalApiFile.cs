using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Restage.Services.Api;

/// <summary>Where the running app's local API is and the token it expects.</summary>
public sealed record LocalApiInfo(int ApiVersion, int Port, string Token, int Pid)
{
    [JsonIgnore]
    public string BaseUrl => $"http://127.0.0.1:{Port}/";
}

/// <summary>
/// The discovery file, %LOCALAPPDATA%\Restage\local-api.json: written by the
/// app when its API is up, deleted when it exits. The port is random and the
/// token new for every run, so a reader needs this file, which only the
/// user's own processes can get at.
/// </summary>
public static class LocalApiFile
{
    public static readonly string Path = System.IO.Path.Combine(Paths.DataDir, "local-api.json");

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Write(LocalApiInfo info)
    {
        Directory.CreateDirectory(Paths.DataDir);
        File.WriteAllText(Path, JsonSerializer.Serialize(info, Json));
    }

    public static void Delete()
    {
        try { File.Delete(Path); } catch { }
    }

    /// <summary>The running app's API, or null when there is no file, it is unreadable, or its process is gone.</summary>
    public static LocalApiInfo? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var info = JsonSerializer.Deserialize<LocalApiInfo>(File.ReadAllText(Path), Json);
            if (info == null || info.Port <= 0 || string.IsNullOrEmpty(info.Token)) return null;
            using var process = Process.GetProcessById(info.Pid);
            return process.HasExited ? null : info;
        }
        catch { return null; }
    }
}
