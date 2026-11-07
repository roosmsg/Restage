using System.Text.Json;
using Restage.Models;
using Restage.Services;
using Xunit;

namespace Restage.Core.Tests;

public class WorkspaceStoreTests
{
    [Fact]
    public void Workspace_round_trips_through_json_with_readable_enums()
    {
        var ws = new Workspace
        {
            Id = "werk-abc123",
            Name = "Werk",
            Desktop = new DesktopRef { Id = Guid.Parse("11111111-2222-3333-4444-555555555555"), Name = "Werk", Index = 1 },
            Wallpaper = @"C:\Pictures\wall.jpg",
            Items =
            {
                new WorkspaceItem
                {
                    Kind = ItemKind.VSCode, Name = "Code", Target = @"C:\Projects\restage",
                    Policy = LaunchPolicy.AlwaysNew,
                    Placements = { ["p1"] = new WindowPlacement { Left = 10, Top = 20, Width = 300, Height = 400, State = WindowShowState.Maximized } },
                },
            },
        };
        ws.Remember(new DisplayProfile { Id = "p1", Name = "1 display · 1920×1080" });

        var json = JsonSerializer.Serialize(ws, WorkspaceStore.JsonOptions);
        Assert.Contains("\"kind\": \"vsCode\"", json);
        Assert.Contains("\"state\": \"maximized\"", json);
        Assert.DoesNotContain("directory", json); // never serialized

        var back = JsonSerializer.Deserialize<Workspace>(json, WorkspaceStore.JsonOptions)!;
        Assert.Equal("Werk", back.Name);
        Assert.Equal(ItemKind.VSCode, back.Items[0].Kind);
        Assert.Equal(LaunchPolicy.AlwaysNew, back.Items[0].Policy);
        Assert.Equal(WindowShowState.Maximized, back.Items[0].PlacementFor("p1")!.State);
        Assert.Equal(ws.Desktop.Id, back.Desktop!.Id);
        Assert.Single(back.Profiles);
    }

    [Fact]
    public void Removed_only_if_open_policy_loads_as_reuse_if_open()
    {
        const string json = """{ "name": "Old", "items": [ { "kind": "generic", "name": "App", "policy": "focusOnly" } ] }""";
        var ws = JsonSerializer.Deserialize<Workspace>(json, WorkspaceStore.JsonOptions)!;
        Assert.Equal(LaunchPolicy.IfNotRunning, ws.Items[0].Policy);
    }

    [Fact]
    public void PlacementFor_falls_back_to_any_profile()
    {
        var item = new WorkspaceItem { Placements = { ["old"] = new WindowPlacement { Left = 5 } } };
        Assert.Equal(5, item.PlacementFor("new")!.Left);
        Assert.Null(new WorkspaceItem().PlacementFor("new"));
    }

    [Theory]
    [InlineData("Werk", "werk-")]
    [InlineData("  Thuis & Studie!! ", "thuis-studie-")]
    [InlineData("", "workspace-")]
    public void NewId_is_a_slug_with_a_random_suffix(string name, string prefix)
    {
        var id = WorkspaceStore.NewId(name);
        Assert.StartsWith(prefix, id);
        Assert.Equal(prefix.Length + 6, id.Length);
        Assert.NotEqual(id, WorkspaceStore.NewId(name));
    }
}
