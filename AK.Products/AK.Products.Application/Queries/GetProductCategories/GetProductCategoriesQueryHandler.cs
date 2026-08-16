using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Queries.GetProductCategories;

public sealed class GetProductCategoriesQueryHandler : IRequestHandler<GetProductCategoriesQuery, IReadOnlyList<string>>
{
    private readonly IUnitOfWork _uow;

    public GetProductCategoriesQueryHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<string>> Handle(GetProductCategoriesQuery request, CancellationToken ct) =>
        await _uow.Products.GetDistinctCategoriesAsync(ct);
}
