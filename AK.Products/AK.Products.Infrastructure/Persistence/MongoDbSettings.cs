namespace AK.Products.Infrastructure.Persistence;

public sealed class MongoDbSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "ACProductsDb";
    public string ProductsCollection { get; set; } = "products";
}
