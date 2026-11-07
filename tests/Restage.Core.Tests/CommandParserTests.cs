using Restage.Services.Api;
using Xunit;

namespace Restage.Core.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("open afstuderen", "afstuderen")]
    [InlineData("Open Afstuderen.", "Afstuderen")]
    [InlineData("Afstuderen", "Afstuderen")]
    [InlineData("switch to research notes", "research notes")]
    [InlineData("Start the Research workspace", "Research")]
    [InlineData("Ga naar Afstuderen alsjeblieft", "Afstuderen")]
    [InlineData("Schakel over naar de werkruimte Afstuderen", "Afstuderen")]
    [InlineData("Hey Restage, open my thesis", "thesis")]
    [InlineData("workspace afstuderen", "afstuderen")]
    [InlineData("show research now", "research")]
    public void Restore_commands_keep_the_name(string text, string name)
    {
        var command = CommandParser.Parse(text);
        Assert.NotNull(command);
        Assert.Equal(CommandParser.Kind.Restore, command.Kind);
        Assert.Equal(name, command.Name);
    }

    [Theory]
    [InlineData("save this as Thesis", "Thesis")]
    [InlineData("Save as Thesis draft", "Thesis draft")]
    [InlineData("capture Thesis", "Thesis")]
    [InlineData("sla op als Scriptie", "Scriptie")]
    [InlineData("Sla dit op als Scriptie, alsjeblieft", "Scriptie")]
    [InlineData("bewaar dit als Werk", "Werk")]
    [InlineData("remember this as Monday", "Monday")]
    [InlineData("save", "")]
    public void Capture_commands_keep_the_name_as_said(string text, string name)
    {
        var command = CommandParser.Parse(text);
        Assert.NotNull(command);
        Assert.Equal(CommandParser.Kind.Capture, command.Kind);
        Assert.Equal(name, command.Name);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Start")]
    public void A_verb_alone_is_taken_as_a_name(string text)
    {
        var command = CommandParser.Parse(text);
        Assert.NotNull(command);
        Assert.Equal(CommandParser.Kind.Restore, command.Kind);
        Assert.Equal(text, command.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("...")]
    public void Nothing_said_is_no_command(string text)
    {
        Assert.Null(CommandParser.Parse(text));
    }
}
