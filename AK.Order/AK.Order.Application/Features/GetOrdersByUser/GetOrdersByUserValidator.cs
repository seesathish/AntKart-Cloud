using FluentValidation;

namespace AK.Order.Application.Features.GetOrdersByUser;

public sealed class GetOrdersByUserValidator : AbstractValidator<GetOrdersByUserQuery>
{
    public GetOrdersByUserValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
