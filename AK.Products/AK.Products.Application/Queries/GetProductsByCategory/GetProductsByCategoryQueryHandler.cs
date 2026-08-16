using AK.Products.Application.Common;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Queries.GetProductsByCategory;

public sealed class GetProductsByCategoryQueryHandler : IRequestHandler<GetProductsByCategoryQuery, IReadOnlyList<ProductDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly IDiscountGrpcClient _discountClient;

    public GetProductsByCategoryQueryHandler(IUnitOfWork uow, IDiscountGrpcClient discountClient)
    {
        _uow = uow;
        _discountClient = discountClient;
    }

    public async Task<IReadOnlyList<ProductDto>> Handle(GetProductsByCategoryQuery request, CancellationToken ct)
    {
        var products = await _uow.Products.GetByCategoryAsync(request.Category, ct);

        var discountTasks = products.Select(p => _discountClient.GetDiscountAsync(p.Id, ct));
        var discounts = await Task.WhenAll(discountTasks);

        return products.Select((p, i) =>
        {
            var d = discounts[i];
            decimal? discountedPrice = null;
            if (d is { IsActive: true })
                discountedPrice = ProductMapper.ComputeDiscountedPrice(p.Price, d.Amount, d.DiscountType);
            return ProductMapper.ToDto(p, discountedPrice);
        }).ToList().AsReadOnly();
    }
}
