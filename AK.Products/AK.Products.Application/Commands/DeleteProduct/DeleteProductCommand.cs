using MediatR;

namespace AK.Products.Application.Commands.DeleteProduct;

public sealed record DeleteProductCommand(string Id) : IRequest<bool>;
