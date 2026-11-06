using Restage.Models;
using Restage.Services;
using Xunit;

namespace Restage.Core.Tests;

public class DisplayProfilesTests
{
    static MonitorInfo Monitor(string device, int left, int width, int height, uint dpi = 96, bool primary = false) =>
        new() { Device = device, Left = left, Top = 0, Width = width, Height = height, Dpi = dpi, IsPrimary = primary };

    [Fact]
    public void Profile_id_depends_on_devices_geometry_and_dpi_but_not_on_order()
    {
        var a = DisplayProfiles.From(new() { Monitor(@"\\.\DISPLAY1", 0, 3840, 2160, 144, true), Monitor(@"\\.\DISPLAY2", 3840, 1920, 1080) });
        var b = DisplayProfiles.From(new() { Monitor(@"\\.\DISPLAY2", 3840, 1920, 1080), Monitor(@"\\.\DISPLAY1", 0, 3840, 2160, 144, true) });
        var c = DisplayProfiles.From(new() { Monitor(@"\\.\DISPLAY1", 0, 3840, 2160, 96, true), Monitor(@"\\.\DISPLAY2", 3840, 1920, 1080) });
        var single = DisplayProfiles.From(new() { Monitor(@"\\.\DISPLAY1", 0, 3840, 2160, 144, true) });

        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, c.Id);
        Assert.NotEqual(a.Id, single.Id);
        Assert.Equal(12, a.Id.Length);
    }

    [Fact]
    public void Profile_name_describes_the_arrangement()
    {
        Assert.Equal("2 displays · 3840×2160 + 1920×1080",
            DisplayProfiles.Describe(new[] { Monitor("a", 0, 3840, 2160), Monitor("b", 3840, 1920, 1080) }));
        Assert.Equal("1 display · 2560×1440", DisplayProfiles.Describe(new[] { Monitor("a", 0, 2560, 1440) }));
        Assert.Equal("No displays", DisplayProfiles.Describe(Array.Empty<MonitorInfo>()));
    }
}
