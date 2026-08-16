using AK.Products.Application.DTOs;
using MediatR;

namespace AK.Products.Application.Commands.UpdateProduct;

public sealed record UpdateProductCommand(string Id, UpdateProductDto Dto) : IRequest<ProductDto>;
