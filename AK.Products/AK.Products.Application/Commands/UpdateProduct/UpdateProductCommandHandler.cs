using AK.Products.Application.Common;
using AK.Products.Application.DTOs;
using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Commands.UpdateProduct;

public sealed class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, ProductDto>
{
    private readonly IUnitOfWork _uow;

    public UpdateProductCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken ct)
    {
        var product = await _uow.Products.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Product '{request.Id}' not found");

        product.Update(request.Dto.Name, request.Dto.Description, request.Dto.Brand,
            request.Dto.Price, request.Dto.StockQuantity, request.Dto.Material);

        await _uow.Products.UpdateAsync(product, ct);
        await _uow.SaveChangesAsync(ct);
        return ProductMapper.ToDto(product);
    }
}
