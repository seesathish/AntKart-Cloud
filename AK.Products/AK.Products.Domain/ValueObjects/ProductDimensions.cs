namespace AK.Products.Domain.ValueObjects;

public record ProductDimensions(decimal Weight, string WeightUnit = "kg", string? Size = null, string? SizeChart = null);
