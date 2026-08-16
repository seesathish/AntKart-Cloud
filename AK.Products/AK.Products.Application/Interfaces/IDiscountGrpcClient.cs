namespace AK.Products.Application.Interfaces;

public sealed record DiscountResult(double Amount, string DiscountType, bool IsActive);

public interface IDiscountGrpcClient
{
    Task<DiscountResult?> GetDiscountAsync(string productId, CancellationToken ct = default);
}
