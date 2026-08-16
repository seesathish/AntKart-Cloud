using AK.Products.Application.DTOs;
using MediatR;

namespace AK.Products.Application.Commands.BulkInsertProducts;

public sealed record BulkInsertProductsCommand(List<CreateProductDto> Products) : IRequest<int>;
