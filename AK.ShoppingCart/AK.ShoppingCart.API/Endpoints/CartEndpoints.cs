using AK.BuildingBlocks.Authentication;
using AK.ShoppingCart.Application.Commands.AddToCart;
using AK.ShoppingCart.Application.Commands.ClearCart;
using AK.ShoppingCart.Application.Commands.RemoveFromCart;
using AK.ShoppingCart.Application.Commands.UpdateCartItem;
using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Application.Queries.GetCart;
using MediatR;

namespace AK.ShoppingCart.API.Endpoints;

// Cart endpoints never expose userId in the URL path — it is always derived from the JWT.
// This prevents IDOR: an authenticated user cannot read or modify another user's cart
// by substituting a different userId in the request.
// The underlying store is Redis (key: AKCart:cart:{userId}) with a 30-day TTL reset on every write.
public static class CartEndpoints
{
    public static void MapCartEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/cart")
            .WithTags("Cart")
            .RequireAuthorization("authenticated");

        // GET /api/v1/cart — returns the full cart with all items and a running total.
        // Returns 404 if the user has never added anything (no cart exists in Redis).
        group.MapGet("/", async (HttpContext http, IMediator mediator, CancellationToken ct) =>
        {
            var userId = http.GetUserId();
            var result = await mediator.Send(new GetCartQuery(userId), ct);
            return result is null
                ? Results.NotFound(new { error = $"Cart for user not found" })
                : Results.Ok(result);
        })
        .WithName("GetCart")
        .WithSummary("Get the authenticated user's cart");

        // POST /api/v1/cart/items — adds a product to the cart.
        // If the product already exists in the cart, the quantity is incremented (not duplicated).
        group.MapPost("/items", async (HttpContext http, AddCartItemDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var userId = http.GetUserId();
            var result = await mediator.Send(new AddToCartCommand(userId, dto), ct);
            return Results.Ok(result);
        })
        .WithName("AddToCart")
        .WithSummary("Add an item to the authenticated user's cart");

        // PUT /api/v1/cart/items/{productId} — sets the quantity for a specific product.
        // Sending quantity=0 removes the item entirely (no separate delete needed for quantity zeroing).
        group.MapPut("/items/{productId}", async (HttpContext http, string productId, UpdateCartItemDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var userId = http.GetUserId();
            var result = await mediator.Send(new UpdateCartItemCommand(userId, productId, dto.Quantity), ct);
            return Results.Ok(result);
        })
        .WithName("UpdateCartItem")
        .WithSummary("Update quantity of a cart item (0 removes the item)");

        // DELETE /api/v1/cart/items/{productId} — removes a single product line from the cart.
        group.MapDelete("/items/{productId}", async (HttpContext http, string productId, IMediator mediator, CancellationToken ct) =>
        {
            var userId = http.GetUserId();
            var result = await mediator.Send(new RemoveFromCartCommand(userId, productId), ct);
            return Results.Ok(result);
        })
        .WithName("RemoveFromCart")
        .WithSummary("Remove an item from the authenticated user's cart");

        // DELETE /api/v1/cart — clears all items.
        // Also triggered automatically by ClearCartOnOrderConfirmedConsumer (message-bus event)
        // after an order is confirmed, so the cart resets without the user manually clearing it.
        group.MapDelete("/", async (HttpContext http, IMediator mediator, CancellationToken ct) =>
        {
            var userId = http.GetUserId();
            await mediator.Send(new ClearCartCommand(userId), ct);
            return Results.NoContent();
        })
        .WithName("ClearCart")
        .WithSummary("Clear all items from the authenticated user's cart");
    }
}
