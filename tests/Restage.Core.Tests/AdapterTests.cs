using Restage.Adapters;
using Restage.Models;
using Restage.Services;
using Xunit;

namespace Restage.Core.Tests;

public class LaunchHelpersTests
{
    [Fact]
    public void Tokenize_honours_quotes_and_rejoins_with_quotes_where_needed()
    {
        var tokens = LaunchHelpers.Tokenize("-P \"My Profile\" --flag=\"a b\" plain");
        Assert.Equal(new[] { "-P", "My Profile", "--flag=a b", "plain" }, tokens);
        Assert.Equal("-P \"My Profile\" \"--flag=a b\" plain", LaunchHelpers.Join(tokens));
    }

    [Theory]
    [InlineData("--updated --start-in-tray", "--updated")]
    [InlineData("/n -Embedding", "")]
    [InlineData("--type=renderer --foo", "")]
    [InlineData(null, "")]
    [InlineData("\"C:\\My Docs\\file.txt\"", "\"C:\\My Docs\\file.txt\"")]
    public void SanitizeArgs_drops_helper_and_start_hidden_flags(string? args, string expected)
    {
        Assert.Equal(expected, LaunchHelpers.SanitizeArgs(args));
    }
}

public class FirefoxAdapterTests
{
    [Fact]
    public void Taskbar_tab_arguments_are_recognised()
    {
        var parsed = FirefoxAdapter.Parse(
            "-taskbar-tab d829367f-3200-4f95-9bb2-386f04aee70f -new-window https://teams.microsoft.com/v2/?clientType=pwa " +
            "-profile C:\\Users\\x\\AppData\\Roaming\\Mozilla\\Firefox\\Profiles\\qloeq5r3.default-esr -container 0");
        Assert.True(parsed.IsTaskbarTab);
        Assert.Equal("d829367f-3200-4f95-9bb2-386f04aee70f", parsed.TaskbarTabId);
        Assert.Equal("https://teams.microsoft.com/v2/?clientType=pwa", parsed.Url);
        Assert.EndsWith("qloeq5r3.default-esr", parsed.ProfilePath);
    }

    [Fact]
    public void Named_profile_round_trips_into_launch_arguments()
    {
        var parsed = FirefoxAdapter.Parse("-P \"Werk Profiel\" -no-remote");
        Assert.Equal("Werk Profiel", parsed.ProfileName);
        Assert.Equal("Werk Profiel", parsed.ProfileKey);
        Assert.Equal("-P \"Werk Profiel\"", FirefoxAdapter.ProfileArgs(parsed));
    }

    [Fact]
    public void Default_profile_has_an_empty_key_and_no_arguments()
    {
        var parsed = FirefoxAdapter.Parse("");
        Assert.Equal("", parsed.ProfileKey);
        Assert.Equal("", FirefoxAdapter.ProfileArgs(parsed));
    }

    [Fact]
    public void Match_distinguishes_profiles_and_taskbar_tabs()
    {
        var adapter = new FirefoxAdapter();
        var exe = @"C:\Program Files\Mozilla Firefox\firefox.exe";
        var browser = new WorkspaceItem { Kind = ItemKind.Firefox, Exe = exe, Target = "", Title = "Mozilla Firefox" };
        var tab = new WorkspaceItem { Kind = ItemKind.FirefoxTaskbarTab, Exe = exe, Target = "abc" };

        var plain = new CapturedWindow { ProcessName = "firefox", ExePath = exe, Args = "", Title = "Mozilla Firefox" };
        var other = new CapturedWindow { ProcessName = "firefox", ExePath = exe, Args = "-P Werk", Title = "Mozilla Firefox" };
        var tabWin = new CapturedWindow { ProcessName = "firefox", ExePath = exe, Args = "-taskbar-tab abc -new-window https://x", Title = "X" };

        Assert.Equal(3, adapter.Match(browser, plain));
        Assert.Equal(0, adapter.Match(browser, other));
        Assert.Equal(0, adapter.Match(browser, tabWin));
        Assert.Equal(3, adapter.Match(tab, tabWin));
        Assert.Equal(0, adapter.Match(tab, plain));
    }
}

public class ChromiumAdapterTests
{
    /// <summary>Google Calendar's app id; Chrome shows it as "kjbdgfilnf" + "pgamdcdgpehopbep" in the default profile.</summary>
    const string Agenda = "kjbdgfilnfhdoflbpgamdcdgpehopbep";
    const string AgendaAumid = "Chrome._crx_kjbdgfilnfpgamdcdgpehopbep";

    [Theory]
    [InlineData("Chrome._crx_abcdefghijklmnopabcdefghijklmnop", "abcdefghijklmnopabcdefghijklmnop")]
    [InlineData("Chrome._crx_ABCDEFGHIJKLMNOPABCDEFGHIJKLMNOP.Profile1", "abcdefghijklmnopabcdefghijklmnop")]
    [InlineData(AgendaAumid, "kjbdgfilnfpgamdcdgpehopbep")]
    [InlineData("Chrome", null)]
    [InlineData("Chrome.Profile1", null)]
    [InlineData(null, null)]
    public void App_id_is_read_from_the_AppUserModelId(string? aumid, string? expected)
    {
        Assert.Equal(expected, ChromiumAdapter.AppIdFromAumid(aumid));
    }

    [Fact]
    public void AppUserModelIds_are_built_the_way_Chrome_shortens_them()
    {
        Assert.Equal("Chrome", ChromiumAdapter.BuildAumid("Chrome", null, "Default"));
        Assert.Equal("Chrome.Profile1", ChromiumAdapter.BuildAumid("Chrome", null, "Profile 1"));
        // Two components share 63 characters: the 37-character app part keeps 15 + 16.
        Assert.Equal(AgendaAumid, ChromiumAdapter.BuildAumid("Chrome", Agenda, "Default"));
        // Three components get 20 each: the app part keeps 10 + 10.
        Assert.Equal("Chrome._crx_kjbdgdgpehopbep.Profile1", ChromiumAdapter.BuildAumid("Chrome", Agenda, "Profile 1"));
    }

    [Fact]
    public void Shortened_app_ids_are_resolved_against_the_installed_apps()
    {
        var installed = new[] { "edanbjnaiofggfmimiidpfmhggkbokck", Agenda };
        Assert.Equal(Agenda, ChromiumAdapter.ResolveAppId(AgendaAumid, "Default", installed));
        Assert.Equal(Agenda, ChromiumAdapter.ResolveAppId("Chrome._crx_kjbdgdgpehopbep.Profile1", "Profile 1", installed));
        Assert.Equal(Agenda, ChromiumAdapter.ResolveAppId($"Chrome._crx_{Agenda}", "Default", Array.Empty<string>()));
        Assert.Equal("kjbdgfilnfpgamdcdgpehopbep", ChromiumAdapter.ResolveAppId(AgendaAumid, "Default", Array.Empty<string>()));
        Assert.Null(ChromiumAdapter.ResolveAppId("Chrome", "Default", installed));
    }

    [Fact]
    public void Profile_segment_maps_back_to_the_real_directory_name()
    {
        Assert.Null(ChromiumAdapter.ProfileFromAumid("Chrome"));
        Assert.Null(ChromiumAdapter.ProfileFromAumid("Chrome._crx_abcdefghijklmnopabcdefghijklmnop"));
        Assert.Null(ChromiumAdapter.ProfileFromAumid(AgendaAumid));
        Assert.Equal("Profile1", ChromiumAdapter.ProfileFromAumid("Chrome.Profile1"));
        Assert.Equal("Profile 1", ChromiumAdapter.ProfileFromAumid("Chrome.Profile1", "Profile 1"));
        Assert.Equal("Profile1", ChromiumAdapter.ProfileFromAumid("Chrome.Profile1", "Profile 2"));
        Assert.Equal("Profile 1", ChromiumAdapter.ProfileFromAumid("Chrome._crx_kjbdgdgpehopbep.Profile1", "Profile 1"));
    }

    [Fact]
    public void Profile_directory_is_read_from_the_command_line()
    {
        Assert.Equal("Profile 2", ChromiumAdapter.ProfileFromArgs("--profile-directory=\"Profile 2\" --flag"));
        Assert.Equal("Default", ChromiumAdapter.ProfileFromArgs("--profile-directory Default"));
        Assert.Null(ChromiumAdapter.ProfileFromArgs("--flag"));
    }

    [Fact]
    public void Browser_windows_and_web_apps_match_their_own_items_only()
    {
        var adapter = new ChromiumAdapter();
        var exe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        var browser = new WorkspaceItem { Kind = ItemKind.Chromium, Exe = exe, Target = "Default", Title = "New Tab - Google Chrome" };
        var app = new WorkspaceItem { Kind = ItemKind.ChromiumApp, Exe = exe, Target = $"Default|{Agenda}" };

        var browserWin = new CapturedWindow { ProcessName = "chrome", ExePath = exe, AppUserModelId = "Chrome", Title = "Docs - Google Chrome" };
        var appWin = new CapturedWindow { ProcessName = "chrome", ExePath = exe, AppUserModelId = AgendaAumid, Title = "Google Agenda - november 2026" };
        var otherApp = new CapturedWindow { ProcessName = "chrome", ExePath = exe, AppUserModelId = "Chrome._crx_edanbjnaiomiidpfmhggkbokck", Title = "Google Translate" };

        Assert.Equal(2, adapter.Match(browser, browserWin));
        Assert.Equal(0, adapter.Match(browser, appWin));
        Assert.Equal(3, adapter.Match(app, appWin));
        Assert.Equal(0, adapter.Match(app, otherApp));
        Assert.Equal(0, adapter.Match(app, browserWin));
    }
}

public class VSCodeAdapterTests
{
    static readonly VSCodeAdapter.KnownWorkspace[] Known =
    {
        new(@"C:\Users\x\Projects\reverse-engineer", new DateTime(2026, 11, 7, 0, 37, 0)),
        new(@"C:\Users\x\Projects\empty", new DateTime(2026, 11, 6)),
        new(@"D:\Archive\empty", new DateTime(2025, 1, 1)),
    };

    [Theory]
    [InlineData("Morluto/rea installation - reverse-engineer", @"C:\Users\x\Projects\reverse-engineer")]
    [InlineData("SnapshotService.cs - reverse-engineer - Visual Studio Code", @"C:\Users\x\Projects\reverse-engineer")]
    [InlineData("● README.md - empty - Visual Studio Code", @"C:\Users\x\Projects\empty")]
    [InlineData("Welcome - Visual Studio Code", null)]
    public void Folder_is_resolved_from_a_title_segment_preferring_the_most_recent(string title, string? expected)
    {
        Assert.Equal(expected, VSCodeAdapter.ResolveTarget(title, Known));
    }

    [Theory]
    [InlineData("file:///c%3A/Users/Djinx/Projects/reverse-engineer", @"C:\Users\Djinx\Projects\reverse-engineer")]
    [InlineData("file:///d:/Obsidian/My%20Vault/", @"D:\Obsidian\My Vault")]
    [InlineData("file://server/share/folder", @"\\server\share\folder")]
    [InlineData("vscode-remote://wsl%2BUbuntu/home/x", null)]
    public void File_uris_become_windows_paths(string uri, string? expected)
    {
        Assert.Equal(expected, VSCodeAdapter.LocalPathFromUri(uri));
    }

    [Fact]
    public void Match_requires_the_folder_name_in_the_title()
    {
        var adapter = new VSCodeAdapter();
        var exe = @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe";
        var item = new WorkspaceItem { Kind = ItemKind.VSCode, Exe = exe, Target = @"C:\Users\x\Projects\reverse-engineer" };
        Assert.Equal(3, adapter.Match(item, new CapturedWindow { ProcessName = "Code", ExePath = exe, Title = "x.cs - reverse-engineer - Visual Studio Code" }));
        Assert.Equal(0, adapter.Match(item, new CapturedWindow { ProcessName = "Code", ExePath = exe, Title = "x.cs - other - Visual Studio Code" }));
        Assert.Equal(0, adapter.Match(item, new CapturedWindow { ProcessName = "Code", ExePath = exe, Args = "--type=renderer", Title = "reverse-engineer" }));
    }
}

public class ObsidianAdapterTests
{
    const string ObsidianJson = """
        { "vaults": {
            "a": { "path": "D:\\Obsidian\\Werkplaats", "ts": 1793569716803 },
            "b": { "path": "D:\\Obsidian\\Afstuderen", "ts": 1793486545600, "open": true },
            "c": { "path": "E:\\Backup\\Afstuderen", "ts": 1700000000000 } },
          "frame": "hidden" }
        """;

    [Theory]
    [InlineData("Untitled - Afstuderen - Obsidian 1.15.0", "Afstuderen")]
    [InlineData("My Note - Werk - Plaats - Obsidian v1.4", "Plaats")]
    [InlineData("Afstuderen - Obsidian 1.15.0", "Afstuderen")]
    [InlineData("Settings", null)]
    [InlineData("Note - Something Else", null)]
    public void Vault_name_is_the_segment_before_Obsidian(string title, string? expected)
    {
        Assert.Equal(expected, ObsidianAdapter.VaultNameFromTitle(title));
    }

    [Theory]
    [InlineData("Untitled - Afstuderen - Obsidian 1.15.0", "Untitled")]
    [InlineData("My - Note - Vault - Obsidian 1.15.0", "My - Note")]
    [InlineData("Afstuderen - Obsidian 1.15.0", null)]
    [InlineData("Settings", null)]
    public void Note_name_is_everything_before_the_vault(string title, string? expected)
    {
        Assert.Equal(expected, ObsidianAdapter.NoteNameFromTitle(title));
    }

    [Fact]
    public void OpenContent_off_opens_the_vault_without_the_note()
    {
        var item = new WorkspaceItem
        {
            Kind = ItemKind.Obsidian, Exe = @"C:\x\Obsidian.exe", Target = @"D:\Obsidian\Afstuderen",
            Args = "obsidian://open?vault=Afstuderen&file=Inleiding",
        };
        Assert.Equal("\"obsidian://open?vault=Afstuderen&file=Inleiding\"", ObsidianAdapter.LaunchArgs(item));
        item.OpenContent = false;
        Assert.Equal("\"obsidian://open?vault=Afstuderen\"", ObsidianAdapter.LaunchArgs(item));

        var generic = new WorkspaceItem { Kind = ItemKind.Generic, Name = "notepad", Args = "\"C:\\notes.txt\"" };
        Assert.Equal("\"C:\\notes.txt\"", GenericAdapter.BuildArgs(generic));
        generic.OpenContent = false;
        Assert.Equal("", GenericAdapter.BuildArgs(generic));
    }

    [Fact]
    public void Open_uri_names_the_vault_and_the_note_as_a_vault_relative_path()
    {
        Assert.Equal("obsidian://open?vault=Mijn%20Vault", ObsidianAdapter.OpenUri("Mijn Vault", null));
        Assert.Equal("obsidian://open?vault=Afstuderen&file=Hoofdstuk%202%2FInleiding",
            ObsidianAdapter.OpenUri("Afstuderen", @"Hoofdstuk 2\Inleiding"));
    }

    [Fact]
    public void Note_path_is_found_inside_the_vault_preferring_markdown_and_recent_files()
    {
        var vault = Path.Combine(Path.GetTempPath(), "restage-vault-" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            Directory.CreateDirectory(Path.Combine(vault, "Old"));
            Directory.CreateDirectory(Path.Combine(vault, "New"));
            Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
            File.WriteAllText(Path.Combine(vault, "Old", "Plan.md"), "");
            File.WriteAllText(Path.Combine(vault, "New", "Plan.md"), "");
            File.WriteAllText(Path.Combine(vault, "Plan.canvas"), "");
            File.WriteAllText(Path.Combine(vault, ".obsidian", "Plan.md"), "");
            File.SetLastWriteTimeUtc(Path.Combine(vault, "Old", "Plan.md"), DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(Path.Combine(vault, "New", "Plan.md"), DateTime.UtcNow);

            Assert.Equal("New/Plan.md", ObsidianAdapter.ResolveNotePath(vault, "Plan"));
            Assert.Null(ObsidianAdapter.ResolveNotePath(vault, "Missing"));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void Vaults_load_open_first_and_resolve_by_folder_name()
    {
        var vaults = ObsidianAdapter.LoadVaults(ObsidianJson);
        Assert.Equal(@"D:\Obsidian\Afstuderen", vaults[0]);
        Assert.Equal(@"D:\Obsidian\Afstuderen", ObsidianAdapter.ResolveVaultPath("afstuderen", vaults));
        Assert.Equal(@"D:\Obsidian\Werkplaats", ObsidianAdapter.ResolveVaultPath("Werkplaats", vaults));
        Assert.Null(ObsidianAdapter.ResolveVaultPath("Nope", vaults));
        Assert.Empty(ObsidianAdapter.LoadVaults(null));
    }
}

public class WorkspaceCaptureServiceTests
{
    [Fact]
    public void Merge_keeps_identity_flags_and_other_profiles_placements()
    {
        var old = new List<WorkspaceItem>
        {
            new()
            {
                Id = "keep", Kind = ItemKind.VSCode, Exe = "code.exe", Target = @"C:\p", Enabled = false, Policy = LaunchPolicy.AlwaysNew,
                Placements = { ["docked"] = new WindowPlacement { Left = 1 }, ["laptop"] = new WindowPlacement { Left = 2 } },
            },
            new() { Id = "gone", Kind = ItemKind.Generic, Exe = "x.exe", Title = "Old" },
        };
        var fresh = new List<WorkspaceItem>
        {
            new() { Kind = ItemKind.VSCode, Exe = "CODE.EXE", Target = @"c:\P", Placements = { ["laptop"] = new WindowPlacement { Left = 9 } } },
            new() { Kind = ItemKind.Generic, Exe = "y.exe", Title = "New" },
        };

        WorkspaceCaptureService.Merge(old, fresh, "laptop");

        Assert.Equal("keep", fresh[0].Id);
        Assert.False(fresh[0].Enabled);
        Assert.Equal(LaunchPolicy.AlwaysNew, fresh[0].Policy);
        Assert.Equal(9, fresh[0].Placements["laptop"].Left);   // current profile: the fresh capture wins
        Assert.Equal(1, fresh[0].Placements["docked"].Left);   // other profile: carried over
        Assert.NotEqual("gone", fresh[1].Id);
    }
}
