using Xunit;
using Restage.Services;

namespace Restage.Core.Tests;

public class RestoreServiceTests
{
    [Fact]
    public void Browser_args_keep_only_profile_and_app_switches()
    {
        var args = RestoreService.SanitizeArgs("chrome",
            "--no-startup-window --profile-directory=\"Profile 2\" --app-id=abc --restore-last-session");
        Assert.Equal("--profile-directory=\"Profile 2\" --app-id=abc", args);
    }

    [Fact]
    public void Embedding_and_child_process_args_are_dropped()
    {
        Assert.Equal("", RestoreService.SanitizeArgs("winword", "/n -Embedding"));
        Assert.Equal("", RestoreService.SanitizeArgs("Code", "--type=renderer --foo"));
    }

    [Fact]
    public void Other_args_pass_through_untouched()
    {
        Assert.Equal("--updated", RestoreService.SanitizeArgs("Signal", "--updated"));
        Assert.Equal("", RestoreService.SanitizeArgs("notepad", null));
    }
}
