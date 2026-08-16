using AK.BuildingBlocks.DDD;

namespace AK.Order.Domain.ValueObjects;

// ShippingAddress is a Value Object — two addresses with identical fields are equal.
// It extends ValueObject (AK.BuildingBlocks.DDD) which derives Equals/GetHashCode/==
// from GetEqualityComponents(), making the structural equality mechanics explicit.
//
// Contrast with Money (AK.Products) which is a C# record — records give you equality
// for free via compiler synthesis. ValueObject base makes the mechanics visible and
// works correctly on plain classes that can't use record syntax (e.g. EF-owned types).
public sealed class ShippingAddress : ValueObject
{
    public string FullName { get; private set; } = string.Empty;
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;

    private ShippingAddress() { }

    public static ShippingAddress Create(
        string fullName, string addressLine1, string? addressLine2,
        string city, string state, string postalCode, string country, string phone)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("FullName is required.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(addressLine1)) throw new ArgumentException("AddressLine1 is required.", nameof(addressLine1));
        if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("City is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("State is required.", nameof(state));
        if (string.IsNullOrWhiteSpace(postalCode)) throw new ArgumentException("PostalCode is required.", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(country)) throw new ArgumentException("Country is required.", nameof(country));
        if (string.IsNullOrWhiteSpace(phone)) throw new ArgumentException("Phone is required.", nameof(phone));

        return new ShippingAddress
        {
            FullName = fullName.Trim(),
            AddressLine1 = addressLine1.Trim(),
            AddressLine2 = addressLine2?.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            PostalCode = postalCode.Trim(),
            Country = country.Trim(),
            Phone = phone.Trim()
        };
    }

    // All 8 fields participate in equality — two addresses are the same only if
    // every component matches, including the optional AddressLine2.
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FullName;
        yield return AddressLine1;
        yield return AddressLine2;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
        yield return Phone;
    }

    public string ToSingleLine() =>
        string.IsNullOrWhiteSpace(AddressLine2)
            ? $"{AddressLine1}, {City}, {State} {PostalCode}, {Country}"
            : $"{AddressLine1}, {AddressLine2}, {City}, {State} {PostalCode}, {Country}";
}
