namespace Restage.Services;

/// <summary>
/// The folders File Explorer currently shows, with the window each one is in,
/// via the Shell.Application COM object. Explorer tabs appear as separate
/// entries sharing one window handle.
/// </summary>
public static class ExplorerWindows
{
    public sealed record Entry(string Path, IntPtr Handle);

    /// <summary>
    /// Runs <see cref="List"/> on a dedicated STA thread (Shell.Application is an
    /// STA object), so callers on the thread pool or a timer can use it. Empty
    /// when the shell does not answer within three seconds.
    /// </summary>
    public static List<Entry> ListSta()
    {
        List<Entry>? result = null;
        var t = new Thread(() => result = List()) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(3000);
        return result ?? new List<Entry>();
    }

    /// <summary>Open Explorer folders. Must run on an STA thread.</summary>
    public static List<Entry> List()
    {
        var result = new List<Entry>();
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return result;
            dynamic shell = Activator.CreateInstance(t)!;
            foreach (var w in shell.Windows())
            {
                try
                {
                    string full = (string)w.FullName;
                    if (!full.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = (string)w.Document.Folder.Self.Path;
                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;
                    var hwnd = new IntPtr((long)w.HWND);
                    if (!result.Any(e => e.Handle == hwnd && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
                        result.Add(new Entry(path, hwnd));
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Comparable form of a folder path: trailing separators dropped, drive roots kept.</summary>
    public static string Normalize(string path) =>
        path.Length > 3 ? path.TrimEnd('\\', '/') : path;
}
