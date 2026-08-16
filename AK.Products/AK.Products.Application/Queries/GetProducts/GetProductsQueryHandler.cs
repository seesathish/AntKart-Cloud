using AK.Products.Application.Common;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Queries.GetProducts;

public sealed class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly IDiscountGrpcClient _discountClient;

    public GetProductsQueryHandler(IUnitOfWork uow, IDiscountGrpcClient discountClient)
    {
        _uow = uow;
        _discountClient = discountClient;
    }

    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        IReadOnlyList<Domain.Entities.Product> products;

        if (!string.IsNullOrWhiteSpace(request.Category))
            products = await _uow.Products.GetByCategoryAsync(request.Category, ct);
        else
            products = await _uow.Products.GetAllAsync(ct);

        if (!string.IsNullOrWhiteSpace(request.SubCategory))
            products = products.Where(p => p.SubCategoryName == request.SubCategory).ToList().AsReadOnly();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Brand.ToLower().Contains(term) ||
                p.Description.ToLower().Contains(term)).ToList().AsReadOnly();
        }

        if (request.IsFeatured.HasValue)
            products = products.Where(p => p.IsFeatured == request.IsFeatured.Value).ToList().AsReadOnly();

        var totalCount = products.Count;
        var paged = products.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();

        var discountTasks = paged.Select(p => _discountClient.GetDiscountAsync(p.Id, ct));
        var discounts = await Task.WhenAll(discountTasks);

        var dtos = paged.Select((p, i) =>
        {
            var d = discounts[i];
            decimal? discountedPrice = null;
            if (d is { IsActive: true })
                discountedPrice = ProductMapper.ComputeDiscountedPrice(p.Price, d.Amount, d.DiscountType);
            return ProductMapper.ToDto(p, discountedPrice);
        }).ToList().AsReadOnly();

        return new PagedResult<ProductDto>(dtos, totalCount, request.Page, request.PageSize);
    }
}
