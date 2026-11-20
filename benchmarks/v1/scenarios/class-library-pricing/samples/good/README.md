# Pricing

A .NET class library that computes an itemised price breakdown from a unit price, a quantity and a tax
rate. Quantity discounts, tax and rounding follow the scenario contract literally.

## Setup

```bash
dotnet restore
```

## Build

```bash
dotnet build --configuration Release
```

## Test

```bash
dotnet test --configuration Release
```

## Public API

- `Pricing.PriceCalculator.Calculate(decimal unitPrice, int quantity, decimal taxRate) : PriceBreakdown`
- `Pricing.PriceBreakdown` with init-only decimal properties
  `GrossSubtotal`, `DiscountRate`, `DiscountAmount`, `NetSubtotal`, `TaxAmount`, `Total`.
- `Pricing.DiscountPolicy.GetDiscountRate(int quantity) : decimal`

## Discount tiers

| Quantity  | Discount |
| --------- | -------- |
| 1 – 9     | 0%       |
| 10 – 49   | 5%       |
| 50 – 99   | 10%      |
| 100+      | 15%      |
