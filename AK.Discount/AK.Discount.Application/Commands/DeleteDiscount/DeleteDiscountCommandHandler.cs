using AK.Discount.Application.Interfaces;
using MediatR;
namespace AK.Discount.Application.Commands.DeleteDiscount;
public sealed class DeleteDiscountCommandHandler(ICouponRepository repo) : IRequestHandler<DeleteDiscountCommand, bool>
{
    public async Task<bool> Handle(DeleteDiscountCommand request, CancellationToken ct)
    {
        var exists = await repo.GetByIdAsync(request.Id, ct);
        if (exists is null) throw new KeyNotFoundException($"Discount with id '{request.Id}' not found.");
        return await repo.DeleteAsync(request.Id, ct);
    }
}
