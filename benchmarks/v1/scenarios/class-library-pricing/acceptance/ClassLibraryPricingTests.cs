using System;
using System.Linq;
using System.Reflection;
using ModelEvaluator.Acceptance;
using Xunit;

namespace ClassLibraryPricing.Acceptance;

/// <summary>
/// Evaluator-owned acceptance checks for the pricing class library. The generated project cannot be
/// referenced from here, so the built library is loaded by reflection and its public API is exercised
/// through <see cref="MethodInfo.Invoke(object?, object?[]?)"/>.
/// </summary>
public sealed class ClassLibraryPricingTests
{
    private static readonly Assembly LibraryAssembly = LoadLibrary();

    private static readonly Type PriceCalculatorType = ResolveType("Pricing.PriceCalculator");
    private static readonly Type PriceBreakdownType = ResolveType("Pricing.PriceBreakdown");
    private static readonly Type DiscountPolicyType = ResolveType("Pricing.DiscountPolicy");

    private static readonly MethodInfo CalculateMethod = ResolveCalculateMethod();
    private static readonly MethodInfo GetDiscountRateMethod = ResolveGetDiscountRateMethod();

    private readonly object _calculator = Activator.CreateInstance(PriceCalculatorType)
        ?? throw new InvalidOperationException("Failed to construct Pricing.PriceCalculator.");

    // -----------------------------------------------------------------------------------------------
    // API shape
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void PriceCalculator_IsPublicSealedClass()
    {
        Assert.True(PriceCalculatorType.IsPublic, "Pricing.PriceCalculator must be public.");
        Assert.True(PriceCalculatorType.IsClass, "Pricing.PriceCalculator must be a class.");
        Assert.True(PriceCalculatorType.IsSealed, "Pricing.PriceCalculator must be sealed.");
    }

    [Fact]
    public void PriceCalculator_Calculate_HasContractSignature()
    {
        var parameters = CalculateMethod.GetParameters();

        Assert.Equal(PriceBreakdownType, CalculateMethod.ReturnType);
        Assert.Equal(3, parameters.Length);

        Assert.Equal("unitPrice", parameters[0].Name);
        Assert.Equal(typeof(decimal), parameters[0].ParameterType);

        Assert.Equal("quantity", parameters[1].Name);
        Assert.Equal(typeof(int), parameters[1].ParameterType);

        Assert.Equal("taxRate", parameters[2].Name);
        Assert.Equal(typeof(decimal), parameters[2].ParameterType);
    }

    [Fact]
    public void PriceBreakdown_IsPublicSealedRecordWithContractProperties()
    {
        Assert.True(PriceBreakdownType.IsPublic, "Pricing.PriceBreakdown must be public.");
        Assert.True(PriceBreakdownType.IsSealed, "Pricing.PriceBreakdown must be sealed.");

        // Records generate a synthetic Clone method named "<Clone>$".
        Assert.NotNull(PriceBreakdownType.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        var expected = new[]
        {
            "GrossSubtotal",
            "DiscountRate",
            "DiscountAmount",
            "NetSubtotal",
            "TaxAmount",
            "Total",
        };

        foreach (var name in expected)
        {
            var property = PriceBreakdownType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(property);
            Assert.Equal(typeof(decimal), property!.PropertyType);

            var setter = property.SetMethod;
            Assert.NotNull(setter);
            // init-only properties have an "IsExternalInit" modreq on the setter's return parameter.
            var modreqs = setter!.ReturnParameter.GetRequiredCustomModifiers();
            Assert.Contains(modreqs, m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }
    }

    [Fact]
    public void DiscountPolicy_IsPublicStaticClassWithContractMethod()
    {
        Assert.True(DiscountPolicyType.IsPublic, "Pricing.DiscountPolicy must be public.");
        Assert.True(DiscountPolicyType.IsAbstract && DiscountPolicyType.IsSealed,
            "Pricing.DiscountPolicy must be a static class.");

        var parameters = GetDiscountRateMethod.GetParameters();
        Assert.Equal(typeof(decimal), GetDiscountRateMethod.ReturnType);
        Assert.Single(parameters);
        Assert.Equal("quantity", parameters[0].Name);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
    }

    // -----------------------------------------------------------------------------------------------
    // Discount tiers
    // -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "0.00")]
    [InlineData(9, "0.00")]
    [InlineData(10, "0.05")]
    [InlineData(49, "0.05")]
    [InlineData(50, "0.10")]
    [InlineData(99, "0.10")]
    [InlineData(100, "0.15")]
    [InlineData(500, "0.15")]
    public void DiscountPolicy_GetDiscountRate_MatchesTiers(int quantity, string expected)
    {
        var actual = (decimal)GetDiscountRateMethod.Invoke(null, new object[] { quantity })!;
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void DiscountPolicy_GetDiscountRate_RejectsNonPositiveQuantity(int quantity)
    {
        var ex = ExpectArgumentOutOfRange(() => GetDiscountRateMethod.Invoke(null, new object[] { quantity }));
        Assert.Equal("quantity", ex.ParamName);
    }

    // -----------------------------------------------------------------------------------------------
    // Calculation pipeline
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Calculate_NoDiscountAndNoTax_ReturnsGrossAsTotal()
    {
        var breakdown = Calculate(9.99m, 1, 0m);
        AssertBreakdown(breakdown,
            gross: 9.99m, discountRate: 0.00m, discountAmount: 0.00m,
            net: 9.99m, tax: 0.00m, total: 9.99m);
    }

    [Fact]
    public void Calculate_TierTwoDiscountAndTax_MatchesWorkedExample()
    {
        var breakdown = Calculate(10m, 10, 0.2m);
        AssertBreakdown(breakdown,
            gross: 100.00m, discountRate: 0.05m, discountAmount: 5.00m,
            net: 95.00m, tax: 19.00m, total: 114.00m);
    }

    [Fact]
    public void Calculate_TierFourDiscountAndTax_MatchesWorkedExample()
    {
        var breakdown = Calculate(2.5m, 100, 0.1m);
        AssertBreakdown(breakdown,
            gross: 250.00m, discountRate: 0.15m, discountAmount: 37.50m,
            net: 212.50m, tax: 21.25m, total: 233.75m);
    }

    [Theory]
    [InlineData(1, "0.00")]
    [InlineData(9, "0.00")]
    [InlineData(10, "0.05")]
    [InlineData(49, "0.05")]
    [InlineData(50, "0.10")]
    [InlineData(99, "0.10")]
    [InlineData(100, "0.15")]
    public void Calculate_UsesTierBoundariesFromDiscountPolicy(int quantity, string expectedRate)
    {
        var breakdown = Calculate(1m, quantity, 0m);
        var expected = decimal.Parse(expectedRate, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, GetProperty(breakdown, "DiscountRate"));
    }

    [Fact]
    public void Calculate_DiscountMidpointRoundsAwayFromZero()
    {
        // Gross = 1.90, discount 5% = 0.095 -> away-from-zero rounds to 0.10.
        var breakdown = Calculate(0.19m, 10, 0m);
        AssertBreakdown(breakdown,
            gross: 1.90m, discountRate: 0.05m, discountAmount: 0.10m,
            net: 1.80m, tax: 0.00m, total: 1.80m);
    }

    [Fact]
    public void Calculate_TaxMidpointRoundsAwayFromZero()
    {
        // Net = 0.15, tax 10% = 0.015 -> away-from-zero rounds to 0.02.
        var breakdown = Calculate(0.15m, 1, 0.1m);
        AssertBreakdown(breakdown,
            gross: 0.15m, discountRate: 0.00m, discountAmount: 0.00m,
            net: 0.15m, tax: 0.02m, total: 0.17m);
    }

    [Fact]
    public void Calculate_GrossSubtotalRoundsAwayFromZero()
    {
        // Unit * quantity = 0.125 -> away-from-zero rounds to 0.13.
        var breakdown = Calculate(0.025m, 5, 0m);
        Assert.Equal(0.13m, GetProperty(breakdown, "GrossSubtotal"));
    }

    [Fact]
    public void Calculate_ZeroUnitPrice_IsValidAndZeroesEveryMonetaryValue()
    {
        var breakdown = Calculate(0m, 25, 0.2m);
        AssertBreakdown(breakdown,
            gross: 0.00m, discountRate: 0.05m, discountAmount: 0.00m,
            net: 0.00m, tax: 0.00m, total: 0.00m);
    }

    [Fact]
    public void Calculate_TaxRateZero_IsValid()
    {
        var breakdown = Calculate(12.34m, 3, 0m);
        Assert.Equal(0.00m, GetProperty(breakdown, "TaxAmount"));
        Assert.Equal(GetProperty(breakdown, "NetSubtotal"), GetProperty(breakdown, "Total"));
    }

    [Fact]
    public void Calculate_TaxRateOne_ProducesTaxEqualToNetAndTotalEqualToDoubleNet()
    {
        var breakdown = Calculate(5m, 1, 1m);
        var net = (decimal)GetProperty(breakdown, "NetSubtotal");
        Assert.Equal(net, (decimal)GetProperty(breakdown, "TaxAmount"));
        Assert.Equal(net * 2m, (decimal)GetProperty(breakdown, "Total"));
    }

    [Fact]
    public void Calculate_DiscountRateIsNotRoundedToTwoDecimals()
    {
        var breakdown = Calculate(1m, 10, 0m);
        // 0.05m has three significant decimal digits including the leading zero. Rounding to 2dp would
        // still give 0.05m, so we assert exact equality rather than a scale count.
        Assert.Equal(0.05m, (decimal)GetProperty(breakdown, "DiscountRate"));
    }

    // -----------------------------------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_RejectsNonPositiveQuantity(int quantity)
    {
        var ex = ExpectArgumentOutOfRange(() => CalculateMethod.Invoke(_calculator, new object[] { 1m, quantity, 0m }));
        Assert.Equal("quantity", ex.ParamName);
    }

    [Fact]
    public void Calculate_RejectsNegativeUnitPrice()
    {
        var ex = ExpectArgumentOutOfRange(() => CalculateMethod.Invoke(_calculator, new object[] { -0.01m, 1, 0m }));
        Assert.Equal("unitPrice", ex.ParamName);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void Calculate_RejectsTaxRateOutsideZeroToOne(string taxRateText)
    {
        var taxRate = decimal.Parse(taxRateText, System.Globalization.CultureInfo.InvariantCulture);
        var ex = ExpectArgumentOutOfRange(() => CalculateMethod.Invoke(_calculator, new object[] { 1m, 1, taxRate }));
        Assert.Equal("taxRate", ex.ParamName);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private object Calculate(decimal unitPrice, int quantity, decimal taxRate)
    {
        try
        {
            return CalculateMethod.Invoke(_calculator, new object[] { unitPrice, quantity, taxRate })
                ?? throw new InvalidOperationException("PriceCalculator.Calculate returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void AssertBreakdown(
        object breakdown,
        decimal gross,
        decimal discountRate,
        decimal discountAmount,
        decimal net,
        decimal tax,
        decimal total)
    {
        Assert.Equal(gross, (decimal)GetProperty(breakdown, "GrossSubtotal"));
        Assert.Equal(discountRate, (decimal)GetProperty(breakdown, "DiscountRate"));
        Assert.Equal(discountAmount, (decimal)GetProperty(breakdown, "DiscountAmount"));
        Assert.Equal(net, (decimal)GetProperty(breakdown, "NetSubtotal"));
        Assert.Equal(tax, (decimal)GetProperty(breakdown, "TaxAmount"));
        Assert.Equal(total, (decimal)GetProperty(breakdown, "Total"));
    }

    private static object GetProperty(object instance, string name)
    {
        var property = PriceBreakdownType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"PriceBreakdown is missing property '{name}'.");
        return property.GetValue(instance) ?? throw new InvalidOperationException($"PriceBreakdown.{name} returned null.");
    }

    private static ArgumentOutOfRangeException ExpectArgumentOutOfRange(Action action)
    {
        try
        {
            action();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException aore)
        {
            return aore;
        }
        catch (ArgumentOutOfRangeException aore)
        {
            return aore;
        }

        throw new Xunit.Sdk.XunitException("Expected ArgumentOutOfRangeException, but no exception was thrown.");
    }

    private static Assembly LoadLibrary()
    {
        var path = EvaluationContext.FindLibraryAssembly("Pricing");
        return Assembly.LoadFrom(path);
    }

    private static Type ResolveType(string fullName)
    {
        return LibraryAssembly.GetType(fullName)
            ?? throw new InvalidOperationException(
                $"Type '{fullName}' was not found in {LibraryAssembly.GetName().Name}. "
                + $"Available public types: {string.Join(", ", LibraryAssembly.GetExportedTypes().Select(t => t.FullName))}.");
    }

    private static MethodInfo ResolveCalculateMethod()
    {
        var method = PriceCalculatorType.GetMethod(
            "Calculate",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(decimal), typeof(int), typeof(decimal) },
            modifiers: null);

        return method
            ?? throw new InvalidOperationException(
                "Pricing.PriceCalculator must declare 'public PriceBreakdown Calculate(decimal, int, decimal)'.");
    }

    private static MethodInfo ResolveGetDiscountRateMethod()
    {
        var method = DiscountPolicyType.GetMethod(
            "GetDiscountRate",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(int) },
            modifiers: null);

        return method
            ?? throw new InvalidOperationException(
                "Pricing.DiscountPolicy must declare 'public static decimal GetDiscountRate(int quantity)'.");
    }
}
