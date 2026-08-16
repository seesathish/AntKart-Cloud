using AK.Products.Domain.Entities;
using AK.Products.Domain.Enums;

namespace AK.Products.Domain.Specifications;

public class FeaturedProductsSpecification : BaseSpecification<Product>
{
    public FeaturedProductsSpecification() => AddCriteria(p => p.IsFeatured && p.Status == ProductStatus.Active);
}
