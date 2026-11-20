using System;
using Pricing;
using Xunit;

namespace Pricing.Tests;

public sealed class DiscountPolicyTests
{
    [Theory]
    [InlineData(1, 0.00)]
    [InlineData(9, 0.00)]
    [InlineData(10, 0.05)]
    [InlineData(49, 0.05)]
    [InlineData(50, 0.10)]
    [InlineData(99, 0.10)]
    [InlineData(100, 0.15)]
    [InlineData(1_000_000, 0.15)]
    public void GetDiscountRate_ReturnsExpectedTier(int quantity, double expected)
    {
        Assert.Equal((decimal)expected, DiscountPolicy.GetDiscountRate(quantity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetDiscountRate_RejectsNonPositiveQuantity(int quantity)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => DiscountPolicy.GetDiscountRate(quantity));
        Assert.Equal("quantity", ex.ParamName);
    }
}
