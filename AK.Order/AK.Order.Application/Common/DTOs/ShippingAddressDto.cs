namespace AK.Order.Application.Common.DTOs;

public sealed record ShippingAddressDto(
    string FullName,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string Phone);
