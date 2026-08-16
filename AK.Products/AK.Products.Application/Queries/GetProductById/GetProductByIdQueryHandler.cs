using AK.Products.Application.Common;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Queries.GetProductById;

// Query handler: fetches a product from MongoDB, then enriches it with a live discount
// from AK.Discount via gRPC before returning the DTO.
//
// Cross-service call pattern: the discount lookup is a best-effort enrichment.
// If AK.Discount is unreachable, DiscountGrpcClient returns null and we return the product
// without a discounted price — the query never fails due to discount service unavailability.
public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly IUnitOfWork _uow;
    private readonly IDiscountGrpcClient _discountClient;

    public GetProductByIdQueryHandler(IUnitOfWork uow, IDiscountGrpcClient discountClient)
    {
        _uow = uow;
        _discountClient = discountClient;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var product = await _uow.Products.GetByIdAsync(request.Id, ct);
        if (product is null) return null;

        // Call AK.Discount via gRPC — returns null if no discount or service is down.
        var discount = await _discountClient.GetDiscountAsync(product.Id, ct);

        decimal? discountedPrice = null;
        if (discount is { IsActive: true })
            // ComputeDiscountedPrice applies Percentage or Fixed formula depending on DiscountType.
            discountedPrice = ProductMapper.ComputeDiscountedPrice(product.Price, discount.Amount, discount.DiscountType);

        // discountedPrice is null if no active discount → ProductDto.DiscountPrice will be null.
        return ProductMapper.ToDto(product, discountedPrice);
    }
}
