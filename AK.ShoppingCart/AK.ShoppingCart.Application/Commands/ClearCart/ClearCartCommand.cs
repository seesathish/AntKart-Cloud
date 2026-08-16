using MediatR;

namespace AK.ShoppingCart.Application.Commands.ClearCart;

public sealed record ClearCartCommand(string UserId) : IRequest<bool>;
