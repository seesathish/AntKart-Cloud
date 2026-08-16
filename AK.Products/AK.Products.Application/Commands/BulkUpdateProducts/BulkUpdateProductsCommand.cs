using AK.Products.Application.DTOs;
using MediatR;

namespace AK.Products.Application.Commands.BulkUpdateProducts;

public sealed record BulkUpdateProductsCommand(List<BulkUpdateProductDto> Updates) : IRequest<int>;
