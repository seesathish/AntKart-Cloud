using AK.Products.Application.DTOs;
using MediatR;

namespace AK.Products.Application.Commands.CreateProduct;

public sealed record CreateProductCommand(CreateProductDto Dto) : IRequest<ProductDto>;
