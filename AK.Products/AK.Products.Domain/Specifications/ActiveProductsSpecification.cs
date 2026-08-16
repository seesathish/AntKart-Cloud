using AK.Products.Domain.Entities;
using AK.Products.Domain.Enums;

namespace AK.Products.Domain.Specifications;

public class ActiveProductsSpecification : BaseSpecification<Product>
{
    public ActiveProductsSpecification() => AddCriteria(p => p.Status == ProductStatus.Active);
}
