using FluentValidation;

namespace AK.Order.Application.Features.GetOrders;

public sealed class GetOrdersValidator : AbstractValidator<GetOrdersQuery>
{
    public GetOrdersValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
