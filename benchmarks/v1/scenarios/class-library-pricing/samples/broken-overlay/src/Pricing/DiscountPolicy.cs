// Deliberately broken variant used to prove that the evaluator detects contract violations.
using System;

namespace Pricing;

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

        // BUG: uses `> 10` (i.e. an inclusive lower bound of 11) instead of `>= 10`.
        if (quantity > 10)
        {
            return 0.05m;
        }

        return 0.00m;
    }
}
