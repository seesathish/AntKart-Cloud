using AK.BuildingBlocks.Authentication;
using AK.Payments.Application.Commands.DeleteSavedCard;
using AK.Payments.Application.Commands.SaveCard;
using AK.Payments.Application.Queries.GetUserSavedCards;
using MediatR;

namespace AK.Payments.API.Endpoints;

public static class SavedCardEndpoints
{
    public static void MapSavedCardEndpoints(this WebApplication app)
    {
        // Saved cards store Razorpay token IDs only — never raw card numbers (PCI compliance).
        // All endpoints derive userId from JWT so a user can only manage their own cards.
        var group = app.MapGroup("/api/payments/cards")
            .WithTags("SavedCards")
            .RequireAuthorization("authenticated");

        // GET /api/payments/cards — current user's saved cards
        group.MapGet("/", async (HttpContext http, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var cards = await mediator.Send(new GetUserSavedCardsQuery(userId));
            return Results.Ok(cards);
        }).WithName("GetMySavedCards");

        // POST /api/payments/cards/save — userId injected from JWT
        group.MapPost("/save", async (HttpContext http, SaveCardRequest req, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var command = new SaveCardCommand(
                userId, req.RazorpayCustomerId, req.RazorpayPaymentId,
                req.CustomerName, req.CustomerEmail, req.CustomerContact);
            var card = await mediator.Send(command);
            return Results.Created($"/api/payments/cards/{card.Id}", card);
        }).WithName("SaveCard");

        // DELETE /api/payments/cards/{id} — userId from JWT for ownership verification in handler
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            await mediator.Send(new DeleteSavedCardCommand(id, userId));
            return Results.NoContent();
        }).WithName("DeleteSavedCard");
    }
}

// userId is not accepted from the client — always derived from JWT
public sealed record SaveCardRequest(
    string RazorpayCustomerId,
    string RazorpayPaymentId,
    string CustomerName,
    string CustomerEmail,
    string CustomerContact);
