namespace Pricing;

/// <summary>
/// The stage-by-stage result of pricing a line item. Every monetary value is rounded to two decimal
/// places with <see cref="System.MidpointRounding.AwayFromZero"/>; <see cref="DiscountRate"/> is a
/// fraction and is not rounded.
/// </summary>
public sealed record PriceBreakdown
{
    public decimal GrossSubtotal { get; init; }
    public decimal DiscountRate { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal NetSubtotal { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal Total { get; init; }
}
