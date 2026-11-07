using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Any application: launched by exe path and sanitized arguments, one launch per
/// captured window. Terminals get their shells' working directories back as
/// tabs; Store apps start through their App Execution Alias.
/// </summary>
public sealed class GenericAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.Generic;

    public override bool CanDescribe(CapturedWindow window) => true;

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var isTerminal = window.ShellCwds is { Count: > 0 }
            || window.ProcessName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase)
            || WindowEnumerator.Shells.Contains(window.ProcessName);
        return new WorkspaceItem
        {
            Kind = isTerminal ? ItemKind.Terminal : ItemKind.Generic,
            Name = window.ProcessName,
            Title = window.Title,
            Exe = window.ExePath,
            Args = LaunchHelpers.SanitizeArgs(window.Args),
            Cwds = window.ShellCwds,
            Detail = window.ShellCwds?.FirstOrDefault() ?? Path.GetFileName(window.ExePath),
        };
    }

    public override int Match(WorkspaceItem item, CapturedWindow window)
    {
        if (!Same(item.Exe, window.ExePath)) return 0;
        return Same(item.Title, window.Title) ? 3 : 1;
    }

    public override Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct)
    {
        var item = group[0];
        var exe = item.Exe;
        var alias = LaunchHelpers.StoreAliasFor(exe);
        if (alias == "") return Task.FromResult(LaunchResult.Failed("Store app without an execution alias; open it from the Start menu"));
        if (alias != null) exe = alias;
        else if (!File.Exists(exe)) return Task.FromResult(LaunchResult.Failed($"not found: {exe}"));

        var args = BuildArgs(item);
        try
        {
            LaunchHelpers.Start(exe, args, item.OpenContent ? item.Cwds?.FirstOrDefault(Directory.Exists) : null);
        }
        catch (Exception ex)
        {
            return Task.FromResult(LaunchResult.Failed(ex.Message));
        }
        var heavy = LaunchHelpers.IsHeavy(item.Name);
        return Task.FromResult(new LaunchResult(true, $"started {Path.GetFileName(exe)} {args}".TrimEnd())
        {
            WindowWait = heavy ? LaunchHelpers.HeavyWait : LaunchHelpers.LightWait,
            Gap = heavy ? LaunchHelpers.HeavyGap : LaunchHelpers.LightGap,
        });
    }

    /// <summary>
    /// Windows Terminal ignores the launcher's working directory, but its CLI
    /// takes one per tab: -d "path" for the first, "; new-tab -d" for the rest.
    /// </summary>
    internal static string BuildArgs(WorkspaceItem item)
    {
        // OpenContent off: the bare app, no file, no folders, no arguments.
        if (!item.OpenContent) return "";
        if (item.Name.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase) && item.Cwds is { Count: > 0 })
        {
            // -w new: each restored entry gets its own window even when the
            // user's settings would otherwise reuse an existing one as a tab.
            var dirs = item.Cwds.Where(Directory.Exists).ToList();
            if (dirs.Count > 0)
                return "-w new " + string.Join(" ; ", dirs.Select((d, i) =>
                    i == 0 ? $"-d \"{d}\"" : $"new-tab -d \"{d}\""));
        }
        return item.Args;
    }
}
