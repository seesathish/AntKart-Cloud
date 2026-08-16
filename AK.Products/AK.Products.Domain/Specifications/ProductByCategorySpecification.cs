using AK.Products.Domain.Entities;

namespace AK.Products.Domain.Specifications;

public class ProductByCategorySpecification : BaseSpecification<Product>
{
    public ProductByCategorySpecification(string categoryName) => AddCriteria(p => p.CategoryName == categoryName);
}
