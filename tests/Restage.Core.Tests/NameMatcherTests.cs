using Restage.Services.Api;
using Xunit;

namespace Restage.Core.Tests;

public class NameMatcherTests
{
    static readonly (string Id, string Name)[] Two = [("a", "Afstuderen"), ("r", "Research")];

    [Theory]
    [InlineData("Afstuderen.")]
    [InlineData("afstuderen")]
    [InlineData("AFSTUDEREN")]
    [InlineData("af studeren")]      // split in two
    [InlineData("afstudeeren")]      // misheard
    [InlineData("afstuderen now")]   // a word around it
    public void Finds_the_workspace_a_phrase_names(string phrase)
    {
        var best = NameMatcher.Best(phrase, Two);
        Assert.NotNull(best);
        Assert.Equal("a", best.Id);
    }

    [Fact]
    public void Exact_name_scores_full()
    {
        Assert.Equal(100, NameMatcher.Best("afstuderen", Two)!.Score);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pizza")]
    [InlineData("the thing")]
    public void Nothing_for_phrases_that_name_no_workspace(string phrase)
    {
        Assert.Null(NameMatcher.Best(phrase, Two));
    }

    [Fact]
    public void A_longer_name_wins_when_the_phrase_says_all_of_it()
    {
        var names = new[] { ("n", "Research notes"), ("r", "Research") };
        Assert.Equal("n", NameMatcher.Best("research notes", names)!.Id);
        Assert.Equal("r", NameMatcher.Best("research", names)!.Id);
    }

    [Fact]
    public void A_tie_starts_nothing()
    {
        var names = new[] { ("1", "Work A"), ("2", "Work B") };
        Assert.Null(NameMatcher.Best("work", names));
        var ranked = NameMatcher.Rank("work", names);
        Assert.Equal(2, ranked.Count);
        Assert.Equal(ranked[0].Score, ranked[1].Score);
    }

    [Theory]
    [InlineData("Café  Noir!", "cafe noir")]
    [InlineData("  Hello, World. ", "hello world")]
    [InlineData("Été 2026", "ete 2026")]
    public void Normalizes_case_accents_and_punctuation(string text, string expected)
    {
        Assert.Equal(expected, NameMatcher.Normalize(text));
    }

    [Fact]
    public void A_spoken_command_parses_then_matches()
    {
        var command = CommandParser.Parse("Open afstuderen, please.");
        Assert.NotNull(command);
        Assert.Equal(CommandParser.Kind.Restore, command.Kind);
        Assert.Equal("a", NameMatcher.Best(command.Name, Two)!.Id);
    }
}
