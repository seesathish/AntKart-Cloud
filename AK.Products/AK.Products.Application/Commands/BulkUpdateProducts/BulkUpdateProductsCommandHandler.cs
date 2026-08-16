using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Commands.BulkUpdateProducts;

public sealed class BulkUpdateProductsCommandHandler : IRequestHandler<BulkUpdateProductsCommand, int>
{
    private readonly IUnitOfWork _uow;

    public BulkUpdateProductsCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<int> Handle(BulkUpdateProductsCommand request, CancellationToken ct)
    {
        var updated = new List<Domain.Entities.Product>();
        foreach (var item in request.Updates)
        {
            var product = await _uow.Products.GetByIdAsync(item.Id, ct);
            if (product is null) continue;
            product.Update(item.Data.Name, item.Data.Description, item.Data.Brand,
                item.Data.Price, item.Data.StockQuantity, item.Data.Material);
            updated.Add(product);
        }
        await _uow.Products.BulkUpdateAsync(updated, ct);
        await _uow.SaveChangesAsync(ct);
        return updated.Count;
    }
}
