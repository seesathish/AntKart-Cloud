using AK.Products.Application.DTOs;
using MediatR;

namespace AK.Products.Application.Queries.GetProducts;

public sealed record GetProductsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Category = null,
    string? SubCategory = null,
    string? SearchTerm = null,
    bool? IsFeatured = null
) : IRequest<PagedResult<ProductDto>>;
