# Contract: pricing class library

## Public API

All types live in the `Pricing` namespace and are declared `public`.

### `PriceBreakdown`

```csharp
public sealed record PriceBreakdown
{
    public decimal GrossSubtotal { get; init; }
    public decimal DiscountRate { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal NetSubtotal { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal Total { get; init; }
}
```

The properties must be declared in exactly this order, must be `init`-only, and must have the exact
names and types shown above.

### `PriceCalculator`

```csharp
public sealed class PriceCalculator
{
    public PriceBreakdown Calculate(decimal unitPrice, int quantity, decimal taxRate);
}
```

The parameter names `unitPrice`, `quantity` and `taxRate` are part of the contract.

### `DiscountPolicy`

```csharp
public static class DiscountPolicy
{
    public static decimal GetDiscountRate(int quantity);
}
```

## Discount tiers

Discounts are chosen from the tier that contains `quantity`, using inclusive lower bounds:

| Quantity | Discount rate |
| -------- | ------------- |
| 1 – 9    | `0.00m`       |
| 10 – 49  | `0.05m`       |
| 50 – 99  | `0.10m`       |
| 100+     | `0.15m`       |

`DiscountRate` is expressed as a decimal fraction (e.g. `0.05m` for 5%) and is **not** rounded to two
decimal places.

## Calculation pipeline

Every monetary value is rounded to two decimal places with
`MidpointRounding.AwayFromZero`, and rounding is applied after each stage in order:

1. `GrossSubtotal   = round(unitPrice * quantity)`
2. `DiscountRate    = DiscountPolicy.GetDiscountRate(quantity)`
3. `DiscountAmount  = round(GrossSubtotal * DiscountRate)`
4. `NetSubtotal     = round(GrossSubtotal - DiscountAmount)`
5. `TaxAmount       = round(NetSubtotal * taxRate)`
6. `Total           = round(NetSubtotal + TaxAmount)`

`Calculate` returns a `PriceBreakdown` whose properties are populated with the values above.

## Validation

`PriceCalculator.Calculate` throws `System.ArgumentOutOfRangeException` in each of the following cases.
The `ParamName` of the thrown exception is graded literally.

| Condition                 | `ParamName`   |
| ------------------------- | ------------- |
| `quantity < 1`            | `"quantity"`  |
| `unitPrice < 0`           | `"unitPrice"` |
| `taxRate < 0` or `> 1`    | `"taxRate"`   |

`DiscountPolicy.GetDiscountRate` throws `System.ArgumentOutOfRangeException` with `ParamName == "quantity"`
when `quantity < 1`.

A `unitPrice` of exactly `0m` is valid. A `taxRate` of exactly `0m` or `1m` is valid.

## Worked examples

- `Calculate(unitPrice: 9.99m, quantity: 1, taxRate: 0m)` →
  `GrossSubtotal = 9.99m`, `DiscountRate = 0.00m`, `DiscountAmount = 0.00m`,
  `NetSubtotal = 9.99m`, `TaxAmount = 0.00m`, `Total = 9.99m`.
- `Calculate(unitPrice: 10m, quantity: 10, taxRate: 0.2m)` →
  `GrossSubtotal = 100.00m`, `DiscountRate = 0.05m`, `DiscountAmount = 5.00m`,
  `NetSubtotal = 95.00m`, `TaxAmount = 19.00m`, `Total = 114.00m`.
- `Calculate(unitPrice: 2.5m, quantity: 100, taxRate: 0.1m)` →
  `GrossSubtotal = 250.00m`, `DiscountRate = 0.15m`, `DiscountAmount = 37.50m`,
  `NetSubtotal = 212.50m`, `TaxAmount = 21.25m`, `Total = 233.75m`.

## Edge cases that are graded

- Tier boundaries at quantities `1`, `9`, `10`, `49`, `50`, `99` and `100` return the exact rate listed
  above.
- Midpoint values in every rounding stage round **away from zero** (for example a discount of
  `0.095m` rounds to `0.10m`, and a tax of `0.015m` rounds to `0.02m`).
- A `unitPrice` of `0m` produces zeroes for every monetary field but still selects the correct discount
  tier for the supplied `quantity`.
- `taxRate = 1m` produces `TaxAmount == NetSubtotal` and `Total == 2 * NetSubtotal`.
- The library performs no I/O, keeps no static state and is thread-safe (i.e. `Calculate` is a pure
  function of its arguments).
