namespace AK.Tools.DiscountSeedLoader;

// A minimal projection of a product read from Cosmos — only the fields the coupon-selection rules
// need. Keeping this separate from the Products domain entity lets the selection logic be unit-tested
// with plain objects, no live Cosmos required.
//   Id  = the product's Mongo `_id` (what AK.Products' GetDiscount(product_id) is keyed on).
public sealed record SeedProduct(
    string Id,
    string Name,
    string Sku,
    string CategoryName,
    string? SubCategoryName,
    decimal Price,
    string Currency);
