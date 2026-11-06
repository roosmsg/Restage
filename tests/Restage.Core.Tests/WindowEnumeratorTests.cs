using Restage.Services;
using Xunit;

namespace Restage.Core.Tests;

public class WindowEnumeratorTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --flag value", "--flag value")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\"", "")]
    [InlineData("C:\\app.exe -a -b", "-a -b")]
    [InlineData("app.exe", "")]
    [InlineData("  \"C:\\x y\\app.exe\"  arg ", "arg")]
    public void StripExeFromCommandLine_drops_the_executable_token(string commandLine, string expected)
    {
        Assert.Equal(expected, WindowEnumerator.StripExeFromCommandLine(commandLine));
    }
}
