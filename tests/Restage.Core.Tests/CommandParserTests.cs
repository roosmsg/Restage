using Restage.Services.Api;
using Xunit;

namespace Restage.Core.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("open portfolio", "portfolio")]
    [InlineData("Open Portfolio.", "Portfolio")]
    [InlineData("Portfolio", "Portfolio")]
    [InlineData("switch to design notes", "design notes")]
    [InlineData("Start the Design workspace", "Design")]
    [InlineData("Ga naar Portfolio alsjeblieft", "Portfolio")]
    [InlineData("Schakel over naar de werkruimte Portfolio", "Portfolio")]
    [InlineData("Hey Restage, open my budget", "budget")]
    [InlineData("workspace portfolio", "portfolio")]
    [InlineData("show design now", "design")]
    public void Restore_commands_keep_the_name(string text, string name)
    {
        var command = CommandParser.Parse(text);
        Assert.NotNull(command);
        Assert.Equal(CommandParser.Kind.Restore, command.Kind);
        Assert.Equal(name, command.Name);
    }

    [Theory]
    [InlineData("save this as Budget", "Budget")]
    [InlineData("Save as Budget draft", "Budget draft")]
    [InlineData("capture Budget", "Budget")]
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
