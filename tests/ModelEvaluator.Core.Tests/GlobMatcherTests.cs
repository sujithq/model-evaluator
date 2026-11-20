using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Tests;

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("src/App/App.csproj", "src/**/*.csproj", true)]
    [InlineData("src/App.csproj", "src/**/*.csproj", true)]
    [InlineData("tests/App.Tests/App.Tests.csproj", "src/**/*.csproj", false)]
    [InlineData("App.sln", "*.sln", true)]
    [InlineData("src/App.sln", "*.sln", false)]
    [InlineData("README.md", "README.md", true)]
    [InlineData("docs/README.md", "README.md", false)]
    [InlineData("src/App/Program.cs", "src/*/Program.cs", true)]
    public void IsMatch_HandlesCommonPatterns(string path, string pattern, bool expected) =>
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));

    [Fact]
    public void AnyMatch_NormalisesWindowsSeparators() =>
        Assert.True(GlobMatcher.AnyMatch([@"src\App\App.csproj"], "src/**/*.csproj"));
}

public sealed class ArgumentParserTests
{
    [Fact]
    public void Split_HandlesQuotesAndWhitespace()
    {
        var arguments = ArgumentParser.Split("--model gpt-x --prompt \"a b c\" --flag '--nested value'");

        Assert.Equal(["--model", "gpt-x", "--prompt", "a b c", "--flag", "--nested value"], arguments);
    }

    [Fact]
    public void Split_PreservesEmptyQuotedArgument() =>
        Assert.Equal(["--title", string.Empty], ArgumentParser.Split("--title \"\""));

    [Fact]
    public void Split_ReturnsEmptyForNullOrWhitespace() =>
        Assert.Empty(ArgumentParser.Split("   "));
}
