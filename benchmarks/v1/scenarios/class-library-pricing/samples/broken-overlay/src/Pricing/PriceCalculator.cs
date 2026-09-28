// Deliberately broken variant used to prove that the evaluator detects contract violations.
using System;

namespace Pricing;

public sealed class PriceCalculator
{
    public PriceBreakdown Calculate(decimal unitPrice, int quantity, decimal taxRate)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "quantity must be at least 1.");
        }

        if (unitPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), unitPrice, "unitPrice must be greater than or equal to 0.");
        }

        // BUG: missing upper-bound validation on taxRate; only the lower bound is checked.
        if (taxRate < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxRate), taxRate, "taxRate must be greater than or equal to 0.");
        }

        var grossSubtotal = Round(unitPrice * quantity);
        var discountRate = DiscountPolicy.GetDiscountRate(quantity);
        var discountAmount = Round(grossSubtotal * discountRate);
        var netSubtotal = Round(grossSubtotal - discountAmount);
        var taxAmount = Round(netSubtotal * taxRate);
        var total = Round(netSubtotal + taxAmount);

        return new PriceBreakdown
        {
            GrossSubtotal = grossSubtotal,
            DiscountRate = discountRate,
            DiscountAmount = discountAmount,
            NetSubtotal = netSubtotal,
            TaxAmount = taxAmount,
            Total = total,
        };
    }

    // BUG: banker's rounding (ToEven) instead of AwayFromZero.
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.ToEven);
}
