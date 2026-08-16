using AK.Products.Application.Common;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using AK.Products.Domain.Entities;
using MediatR;

namespace AK.Products.Application.Commands.CreateProduct;

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly IUnitOfWork _uow;

    public CreateProductCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var dto = request.Dto;
        if (await _uow.Products.SkuExistsAsync(dto.SKU, ct))
            throw new InvalidOperationException($"Product with SKU '{dto.SKU}' already exists");

        var product = Product.Create(
            dto.Name, dto.Description, dto.SKU, dto.Brand,
            dto.CategoryName, dto.SubCategoryName,
            dto.Price, dto.Currency, dto.StockQuantity,
            dto.Sizes, dto.Colors, dto.Material);

        await _uow.Products.AddAsync(product, ct);
        await _uow.SaveChangesAsync(ct);
        return ProductMapper.ToDto(product);
    }
}
