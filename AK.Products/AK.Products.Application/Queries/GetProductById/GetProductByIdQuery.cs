using AK.Products.Application.DTOs;
using MediatR;

namespace AK.Products.Application.Queries.GetProductById;

public sealed record GetProductByIdQuery(string Id) : IRequest<ProductDto?>;
