using TaskBoard;

namespace TaskBoard.Tests;

public sealed class TaskValidatorTests
{
    [Fact]
    public void TryValidate_TrimmedTitle_IsAccepted()
    {
        var ok = TaskValidator.TryValidate("  Buy milk  ", out var title, out var error);

        Assert.True(ok);
        Assert.Equal("Buy milk", title);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_BlankTitle_ReturnsRequiredMessage(string? raw)
    {
        var ok = TaskValidator.TryValidate(raw, out _, out var error);

        Assert.False(ok);
        Assert.Equal("Title is required.", error);
    }

    [Fact]
    public void TryValidate_TitleLongerThanMax_IsRejected()
    {
        var raw = new string('a', 201);

        var ok = TaskValidator.TryValidate(raw, out _, out var error);

        Assert.False(ok);
        Assert.Equal("Title must be 200 characters or fewer.", error);
    }

    [Fact]
    public void TryValidate_TitleAtBoundary_IsAccepted()
    {
        var raw = new string('a', 200);

        var ok = TaskValidator.TryValidate(raw, out var title, out _);

        Assert.True(ok);
        Assert.Equal(raw, title);
    }
}
