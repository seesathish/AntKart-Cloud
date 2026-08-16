using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using AK.Products.Domain.Entities;
using MediatR;

namespace AK.Products.Application.Commands.BulkInsertProducts;

public sealed class BulkInsertProductsCommandHandler : IRequestHandler<BulkInsertProductsCommand, int>
{
    private readonly IUnitOfWork _uow;

    public BulkInsertProductsCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<int> Handle(BulkInsertProductsCommand request, CancellationToken ct)
    {
        var products = request.Products.Select(dto => Product.Create(
            dto.Name, dto.Description, dto.SKU, dto.Brand,
            dto.CategoryName, dto.SubCategoryName,
            dto.Price, dto.Currency, dto.StockQuantity,
            dto.Sizes, dto.Colors, dto.Material)).ToList();

        await _uow.Products.BulkInsertAsync(products, ct);
        await _uow.SaveChangesAsync(ct);
        return products.Count;
    }
}
