using Microsoft.Win32;

namespace Restage.Services;

/// <summary>
/// The restage: URI scheme, registered per user so scripts and voice tools can
/// open "restage://restore/Werk". Registration is idempotent and rewrites the
/// command when the exe has moved.
/// </summary>
public static class UriScheme
{
    public const string Scheme = "restage";

    public static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            key.SetValue("", "URL:Restage workspace");
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var cmd = key.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch { /* registration is a convenience; never fail startup over it */ }
    }

    /// <summary>
    /// The workspace a command line asks to restore: "--restore &lt;name&gt;" or a
    /// "restage://restore/&lt;name&gt;" URI. Null when there is no such request.
    /// </summary>
    public static string? RestoreRequest(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Equals("--restore", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return string.Join(' ', args.Skip(i + 1).TakeWhile(x => !x.StartsWith("--"))).Trim();
            if (a.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(a, UriKind.Absolute, out var uri)
                && uri.Host.Equals("restore", StringComparison.OrdinalIgnoreCase))
            {
                var name = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
                if (name.Length > 0) return name;
            }
        }
        return null;
    }
}
