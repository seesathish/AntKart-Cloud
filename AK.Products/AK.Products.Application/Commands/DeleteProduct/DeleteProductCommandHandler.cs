using AK.Products.Application.Interfaces;
using MediatR;

namespace AK.Products.Application.Commands.DeleteProduct;

public sealed class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, bool>
{
    private readonly IUnitOfWork _uow;

    public DeleteProductCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<bool> Handle(DeleteProductCommand request, CancellationToken ct)
    {
        if (!await _uow.Products.ExistsAsync(request.Id, ct))
            throw new KeyNotFoundException($"Product '{request.Id}' not found");

        await _uow.Products.DeleteAsync(request.Id, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
