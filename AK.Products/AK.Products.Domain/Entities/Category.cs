using AK.BuildingBlocks.DDD;

namespace AK.Products.Domain.Entities;

public class Category : StringEntity
{
    private Category() { }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? ParentCategoryId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Category Create(string name, string slug, string? description = null, string? parentCategoryId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return new Category
        {
            Name = name,
            Slug = slug.ToLower(),
            Description = description,
            ParentCategoryId = parentCategoryId
        };
    }

    public void Update(string name, string slug, string? description)
    {
        Name = name;
        Slug = slug.ToLower();
        Description = description;
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate() { IsActive = true; SetUpdatedAt(); }
}
