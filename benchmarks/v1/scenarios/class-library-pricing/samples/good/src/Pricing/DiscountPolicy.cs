using System;

namespace Pricing;

/// <summary>
/// Quantity-based discount policy. Tiers are graded literally by <see cref="GetDiscountRate"/>.
/// </summary>
public static class DiscountPolicy
{
    public static decimal GetDiscountRate(int quantity)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "quantity must be at least 1.");
        }

        if (quantity >= 100)
        {
            return 0.15m;
        }

        if (quantity >= 50)
        {
            return 0.10m;
        }

        if (quantity >= 10)
        {
            return 0.05m;
        }

        return 0.00m;
    }
}
