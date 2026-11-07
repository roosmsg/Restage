using Restage.Models;
using Restage.Native;
using Restage.Services;

namespace Restage.Cli;

/// <summary>
/// restage capture &lt;name&gt; | update &lt;name|id&gt; | list | show &lt;name|id&gt; |
/// restore &lt;name|id&gt; [--force] [--here] | delete &lt;id&gt; | inspect
/// </summary>
static class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") return Usage(0);
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "capture" => Capture(args.Skip(1).ToArray(), update: false),
                "update" => Capture(args.Skip(1).ToArray(), update: true),
                "list" => List(),
                "show" => Show(args.ElementAtOrDefault(1)),
                "restore" => await Restore(args.Skip(1).ToArray()),
                "delete" => Delete(args.ElementAtOrDefault(1)),
                "inspect" => Inspect(args.Skip(1).Contains("--windows")),
                _ => Usage(2),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    static int Usage(int code)
    {
        Console.WriteLine("""
            restage — save and restore desktop workspaces

              restage capture <name>              save the desktop as a new workspace
              restage update <name|id>            re-capture an existing workspace (keeps its settings)
              restage list                        list workspaces
              restage show <name|id>              print a workspace's items
              restage restore <name|id> [--force] [--here]
                                                  bring a workspace back; --force relaunches apps that
                                                  are already open, --here skips the desktop switch
              restage delete <id>                 delete a workspace
              restage inspect [--windows]         print what a capture would record, without saving;
                                                  --windows also lists every window with its
                                                  AppUserModelID and arguments (adapter debugging)
            """);
        return code;
    }

    static int Capture(string[] rest, bool update)
    {
        var name = string.Join(' ', rest).Trim();
        if (name.Length == 0) return Usage(2);
        Workspace? existing = null;
        if (update)
        {
            existing = WorkspaceStore.Find(name);
            if (existing == null) { Console.Error.WriteLine($"no workspace named \"{name}\""); return 1; }
        }
        var result = WorkspaceCaptureService.Capture(existing?.Name ?? name, existing);
        WorkspaceStore.Save(result.Workspace, result.ThumbnailPng);
        Console.WriteLine($"{(update ? "Updated" : "Saved")} \"{result.Workspace.Name}\" ({result.Workspace.Id})");
        PrintWorkspace(result.Workspace, result.Profile.Id);
        return 0;
    }

    static int Inspect(bool windows)
    {
        var result = WorkspaceCaptureService.Capture("(inspect)", takeThumbnail: false);
        PrintWorkspace(result.Workspace, result.Profile.Id);
        if (!windows) return 0;
        Console.WriteLine("  windows:");
        foreach (var w in WindowEnumerator.Enumerate())
        {
            Console.WriteLine($"    {w.ProcessName,-14} {Trunc(w.Title, 70)}");
            Console.WriteLine($"                   aumid: {w.AppUserModelId ?? "-"}");
            if (w.Args.Length > 0) Console.WriteLine($"                   args:  {Trunc(w.Args, 100)}");
        }
        return 0;
    }

    static int List()
    {
        var all = WorkspaceStore.List();
        if (all.Count == 0) { Console.WriteLine("No workspaces yet. Try: restage capture <name>"); return 0; }
        foreach (var ws in all)
            Console.WriteLine($"{ws.Id,-40} {ws.Name,-24} {ws.Items.Count,3} items  desktop: {ws.Desktop?.DisplayName ?? "-",-12} updated {ws.UpdatedAt:yyyy-MM-dd HH:mm}");
        return 0;
    }

    static int Show(string? idOrName)
    {
        if (idOrName == null) return Usage(2);
        var ws = WorkspaceStore.Find(idOrName);
        if (ws == null) { Console.Error.WriteLine($"no workspace \"{idOrName}\""); return 1; }
        PrintWorkspace(ws, DisplayProfiles.Current().Id);
        return 0;
    }

    static int Delete(string? id)
    {
        if (id == null) return Usage(2);
        var ws = WorkspaceStore.Find(id);
        if (ws == null) { Console.Error.WriteLine($"no workspace \"{id}\""); return 1; }
        WorkspaceStore.Delete(ws.Id);
        Console.WriteLine($"Deleted \"{ws.Name}\" ({ws.Id})");
        return 0;
    }

    static async Task<int> Restore(string[] rest)
    {
        var force = rest.Contains("--force");
        var here = rest.Contains("--here");
        var idOrName = string.Join(' ', rest.Where(a => !a.StartsWith("--"))).Trim();
        if (idOrName.Length == 0) return Usage(2);
        var ws = WorkspaceStore.Find(idOrName);
        if (ws == null) { Console.Error.WriteLine($"no workspace \"{idOrName}\""); return 1; }

        var names = ws.Items.ToDictionary(i => i.Id, i => i.Name);
        var progress = new Progress<RestoreEvent>(e =>
        {
            var who = e.ItemId != null && names.TryGetValue(e.ItemId, out var n) ? n : "";
            Console.WriteLine($"{e.Status,-10} {who,-18} {e.Message}");
        });
        var options = new RestoreOptions
        {
            Force = force,
            SwitchDesktop = here ? null : async (desktop, ct) =>
            {
                Console.WriteLine($"Switch to desktop \"{desktop.DisplayName}\" (Win+Ctrl+Left/Right); waiting up to 60 s…");
                return desktop.Id is { } id && await VirtualDesktops.WaitUntilCurrentAsync(id, TimeSpan.FromSeconds(60), ct);
            },
        };
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        await WorkspaceRestoreService.RestoreAsync(ws, options, progress, cts.Token);
        // Progress<T> posts to the thread pool; give the last lines a moment.
        await Task.Delay(100);
        return 0;
    }

    static void PrintWorkspace(Workspace ws, string profileId)
    {
        Console.WriteLine($"Workspace: {ws.Name}");
        Console.WriteLine($"  desktop:   {ws.Desktop?.DisplayName ?? "-"}{(ws.Desktop?.Id is { } g ? $" ({g})" : "")}");
        Console.WriteLine($"  wallpaper: {ws.Wallpaper ?? "-"}");
        Console.WriteLine($"  profiles:  {string.Join("; ", ws.Profiles.Select(p => $"{p.Name} [{p.Id}]{(p.Id == profileId ? " *" : "")}"))}");
        Console.WriteLine($"  items ({ws.Items.Count}):");
        foreach (var i in ws.Items)
        {
            var p = i.PlacementFor(profileId);
            var where = p == null ? "" : $"{p.State} {p.Left},{p.Top} {p.Width}x{p.Height}";
            Console.WriteLine($"    [{i.Kind,-17}] {i.Name,-16} {Trunc(i.Detail ?? i.Title, 48),-50} {where}");
            if (i.Kind is ItemKind.Generic or ItemKind.Terminal or ItemKind.ChromiumApp && i.Args.Length > 0)
                Console.WriteLine($"                        args: {Trunc(i.Args, 90)}");
        }
    }

    static string Trunc(string s, int n) => s.Length > n ? s[..(n - 1)] + "…" : s;
}
