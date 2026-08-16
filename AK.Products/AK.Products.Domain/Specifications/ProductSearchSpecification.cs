using AK.Products.Domain.Entities;
using AK.Products.Domain.Enums;

namespace AK.Products.Domain.Specifications;

public class ProductSearchSpecification : BaseSpecification<Product>
{
    public ProductSearchSpecification(string? searchTerm, string? categoryName, string? subCategoryName,
        ProductStatus? status, int skip = 0, int take = 20)
    {
        AddCriteria(p =>
            (string.IsNullOrEmpty(searchTerm) || p.Name.Contains(searchTerm) || p.SKU.Contains(searchTerm)) &&
            (string.IsNullOrEmpty(categoryName) || p.CategoryName == categoryName) &&
            (string.IsNullOrEmpty(subCategoryName) || p.SubCategoryName == subCategoryName) &&
            (!status.HasValue || p.Status == status));
        AddOrderByDescending(p => p.CreatedAt);
        ApplyPaging(skip, take);
    }
}
