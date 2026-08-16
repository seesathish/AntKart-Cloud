using AK.Order.Application.Common.DTOs;

namespace AK.Order.Application.Features.CreateOrder;

// Outcome of a create-order attempt. The order is always priced at the catalogue's effective price
// (server-authoritative); these statuses report whether the attempt succeeded or why it was stopped.
public enum CreateOrderStatus
{
    Success,             // priced from the catalogue and persisted
    PriceChanged,        // a price INCREASED vs the customer's submitted price — interrupt to confirm
    ProductUnavailable,  // a product is not found or not active — cannot be ordered
    PricingUnavailable   // the catalogue could not be reached — fail closed, nothing persisted
}

// One per-line problem surfaced to the client. SubmittedPrice is what the client sent; CurrentPrice
// is the catalogue's effective price (null when the product was not found).
//   Reason ∈ { PriceIncreased, ProductNotFound, ProductInactive }.
public sealed record PriceProblem(string ProductId, decimal SubmittedPrice, decimal? CurrentPrice, string Reason);

public sealed class CreateOrderResult
{
    public CreateOrderStatus Status { get; }
    public OrderDto? Order { get; }
    public IReadOnlyList<PriceProblem> Problems { get; }

    private CreateOrderResult(CreateOrderStatus status, OrderDto? order, IReadOnlyList<PriceProblem> problems)
    {
        Status = status;
        Order = order;
        Problems = problems;
    }

    public static CreateOrderResult Success(OrderDto order) =>
        new(CreateOrderStatus.Success, order, []);

    public static CreateOrderResult PriceChanged(IReadOnlyList<PriceProblem> problems) =>
        new(CreateOrderStatus.PriceChanged, null, problems);

    public static CreateOrderResult ProductUnavailable(IReadOnlyList<PriceProblem> problems) =>
        new(CreateOrderStatus.ProductUnavailable, null, problems);

    public static CreateOrderResult PricingUnavailable() =>
        new(CreateOrderStatus.PricingUnavailable, null, []);
}
