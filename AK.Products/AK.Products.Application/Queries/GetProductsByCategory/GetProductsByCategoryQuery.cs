using AK.Products.Application.DTOs;
using MediatR;

namespace AK.Products.Application.Queries.GetProductsByCategory;

public sealed record GetProductsByCategoryQuery(string Category) : IRequest<IReadOnlyList<ProductDto>>;
