namespace AK.Products.Domain.ValueObjects;

public record ProductImage(string Url, string AltText, bool IsPrimary = false);
