using System;
using Pricing;
using Xunit;

namespace Pricing.Tests;

public sealed class PriceCalculatorTests
{
    private readonly PriceCalculator _calculator = new();

    [Fact]
    public void Calculate_NoDiscountAndNoTax_ReturnsGrossAsTotal()
    {
        var result = _calculator.Calculate(unitPrice: 9.99m, quantity: 1, taxRate: 0m);

        Assert.Equal(9.99m, result.GrossSubtotal);
        Assert.Equal(0.00m, result.DiscountRate);
        Assert.Equal(0.00m, result.DiscountAmount);
        Assert.Equal(9.99m, result.NetSubtotal);
        Assert.Equal(0.00m, result.TaxAmount);
        Assert.Equal(9.99m, result.Total);
    }

    [Fact]
    public void Calculate_TierTwoDiscountAndTax_AppliesRoundingAfterEveryStage()
    {
        var result = _calculator.Calculate(unitPrice: 10m, quantity: 10, taxRate: 0.2m);

        Assert.Equal(100.00m, result.GrossSubtotal);
        Assert.Equal(0.05m, result.DiscountRate);
        Assert.Equal(5.00m, result.DiscountAmount);
        Assert.Equal(95.00m, result.NetSubtotal);
        Assert.Equal(19.00m, result.TaxAmount);
        Assert.Equal(114.00m, result.Total);
    }

    [Fact]
    public void Calculate_TierFourDiscountAndTax_ProducesTheDocumentedNumbers()
    {
        var result = _calculator.Calculate(unitPrice: 2.5m, quantity: 100, taxRate: 0.1m);

        Assert.Equal(250.00m, result.GrossSubtotal);
        Assert.Equal(0.15m, result.DiscountRate);
        Assert.Equal(37.50m, result.DiscountAmount);
        Assert.Equal(212.50m, result.NetSubtotal);
        Assert.Equal(21.25m, result.TaxAmount);
        Assert.Equal(233.75m, result.Total);
    }

    [Fact]
    public void Calculate_MidpointDiscountRoundsAwayFromZero()
    {
        // Gross = 1.90, discount 5% = 0.095 -> rounded away from zero = 0.10.
        var result = _calculator.Calculate(unitPrice: 0.19m, quantity: 10, taxRate: 0m);

        Assert.Equal(1.90m, result.GrossSubtotal);
        Assert.Equal(0.10m, result.DiscountAmount);
        Assert.Equal(1.80m, result.NetSubtotal);
        Assert.Equal(1.80m, result.Total);
    }

    [Fact]
    public void Calculate_MidpointTaxRoundsAwayFromZero()
    {
        // Net = 0.15, tax 10% = 0.015 -> rounded away from zero = 0.02.
        var result = _calculator.Calculate(unitPrice: 0.15m, quantity: 1, taxRate: 0.1m);

        Assert.Equal(0.15m, result.GrossSubtotal);
        Assert.Equal(0.02m, result.TaxAmount);
        Assert.Equal(0.17m, result.Total);
    }

    [Fact]
    public void Calculate_ZeroUnitPrice_IsValidAndZeroesEveryMonetaryValue()
    {
        var result = _calculator.Calculate(unitPrice: 0m, quantity: 25, taxRate: 0.2m);

        Assert.Equal(0.00m, result.GrossSubtotal);
        Assert.Equal(0.05m, result.DiscountRate);
        Assert.Equal(0.00m, result.DiscountAmount);
        Assert.Equal(0.00m, result.NetSubtotal);
        Assert.Equal(0.00m, result.TaxAmount);
        Assert.Equal(0.00m, result.Total);
    }

    [Fact]
    public void Calculate_TaxRateOne_DoublesTheNetSubtotal()
    {
        var result = _calculator.Calculate(unitPrice: 5m, quantity: 1, taxRate: 1m);

        Assert.Equal(5.00m, result.GrossSubtotal);
        Assert.Equal(5.00m, result.TaxAmount);
        Assert.Equal(10.00m, result.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Calculate_RejectsNonPositiveQuantity(int quantity)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(1m, quantity, 0m));
        Assert.Equal("quantity", ex.ParamName);
    }

    [Fact]
    public void Calculate_RejectsNegativeUnitPrice()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(-0.01m, 1, 0m));
        Assert.Equal("unitPrice", ex.ParamName);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Calculate_RejectsTaxRateOutsideZeroToOne(double taxRate)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(1m, 1, (decimal)taxRate));
        Assert.Equal("taxRate", ex.ParamName);
    }
}
