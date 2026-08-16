using AK.Products.Domain.Entities;
using AK.Products.Domain.Specifications;

namespace AK.Products.Application.Interfaces;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetByCategoryAsync(string category, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Product>> ListAsync(ISpecification<Product> spec, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<Product> spec, CancellationToken ct = default);
    Task<Product> AddAsync(Product product, CancellationToken ct = default);
    Task UpdateAsync(Product product, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task BulkInsertAsync(IEnumerable<Product> products, CancellationToken ct = default);
    Task BulkUpdateAsync(IEnumerable<Product> products, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
    Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
