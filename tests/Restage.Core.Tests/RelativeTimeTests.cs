using Restage.Services;
using Xunit;

namespace Restage.Core.Tests;

public class RelativeTimeTests
{
    static readonly DateTime Now = new(2026, 11, 7, 14, 30, 0);

    [Theory]
    [InlineData(0, 0, 30, "just now")]
    [InlineData(0, 1, 0, "1 minute ago")]
    [InlineData(0, 25, 0, "25 minutes ago")]
    [InlineData(1, 0, 0, "1 hour ago")]
    [InlineData(5, 59, 0, "5 hours ago")]
    [InlineData(23, 0, 0, "23 hours ago")]
    [InlineData(30, 0, 0, "yesterday")]
    [InlineData(72, 0, 0, "3 days ago")]
    [InlineData(24 * 6, 0, 0, "6 days ago")]
    [InlineData(24 * 7, 0, 0, "31 Oct")]
    [InlineData(24 * 400, 0, 0, "3 Oct 2025")]
    public void Ago_reads_naturally(int hours, int minutes, int seconds, string expected)
    {
        var then = Now - new TimeSpan(hours, minutes, seconds);
        Assert.Equal(expected, RelativeTime.Ago(then, Now));
    }

    [Fact]
    public void Date_names_today_and_yesterday_and_drops_the_current_year()
    {
        Assert.Equal("today", RelativeTime.Date(Now.AddHours(-3), Now));
        Assert.Equal("yesterday", RelativeTime.Date(Now.AddDays(-1), Now));
        Assert.Equal("2 Nov", RelativeTime.Date(Now.AddDays(-5), Now));
        Assert.Equal("25 Dec 2025", RelativeTime.Date(new DateTime(2025, 12, 25), Now));
    }
}
