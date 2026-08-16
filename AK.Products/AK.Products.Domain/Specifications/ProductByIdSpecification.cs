using AK.Products.Domain.Entities;

namespace AK.Products.Domain.Specifications;

public class ProductByIdSpecification : BaseSpecification<Product>
{
    public ProductByIdSpecification(string id) => AddCriteria(p => p.Id == id);
}
