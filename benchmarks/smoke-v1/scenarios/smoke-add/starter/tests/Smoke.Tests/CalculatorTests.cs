using Xunit;

namespace Smoke.Tests;

public sealed class CalculatorTests
{
    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(-4, 1, -3)]
    public void Add_ReturnsSum(int left, int right, int expected) =>
        Assert.Equal(expected, Calculator.Add(left, right));
}
