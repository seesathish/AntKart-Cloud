using AK.Products.Application.Commands.BulkInsertProducts;
using AK.Products.Application.Commands.BulkUpdateProducts;
using AK.Products.Application.Commands.CreateProduct;
using AK.Products.Application.Commands.DeleteProduct;
using AK.Products.Application.Commands.UpdateProduct;
using AK.Products.Application.DTOs;
using AK.Products.Application.Queries.GetProductById;
using AK.Products.Application.Queries.GetProductCategories;
using AK.Products.Application.Queries.GetProducts;
using AK.Products.Application.Queries.GetProductsByCategory;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AK.Products.API.Endpoints;

// Product catalogue endpoints. All read endpoints are public (AllowAnonymous) so the storefront
// doesn't need authentication. Write endpoints (create, update, delete, bulk ops) require the
// "admin" app role. Categories are data-driven strings — no hardcoded enum — so adding
// a new category (e.g. "Sports") only requires inserting products with that category value.
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products");

        // GET /api/v1/products — paged, filterable product list.
        // All query params are optional; invalid page/pageSize values are clamped to 1/20.
        // Supports ?category=Men, ?subCategory=Shirts, ?search=polo, ?featured=true in any combination.
        group.MapGet("/", async (
            IMediator mediator,
            CancellationToken ct,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? category = null,
            [FromQuery] string? subCategory = null,
            [FromQuery] string? search = null,
            [FromQuery] bool? featured = null) =>
        {
            var query = new GetProductsQuery(
                Page: (page ?? 1) > 0 ? (page ?? 1) : 1,
                PageSize: (pageSize ?? 20) > 0 ? (pageSize ?? 20) : 20,
                Category: category,
                SubCategory: subCategory,
                SearchTerm: search,
                IsFeatured: featured);
            var result = await mediator.Send(query, ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetProducts")
        .WithSummary("Get products — filter by ?category=Men&subCategory=Shirts");

        // GET /api/v1/products/categories — returns distinct top-level category names from MongoDB.
        // Used by the frontend to populate the category navigation bar dynamically.
        // Adding a new category requires no code change — just seed products with the new category name.
        group.MapGet("/categories", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductCategoriesQuery(), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetProductCategories")
        .WithSummary("Get all distinct top-level category names from the product catalogue");

        // GET /api/v1/products/featured — shortcut for ?featured=true with a larger page size.
        // Used on the homepage hero/banner section.
        group.MapGet("/featured", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductsQuery(IsFeatured: true, PageSize: 50), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetFeaturedProducts")
        .WithSummary("Get all featured products");

        // GET /api/v1/products/{id} — single product by its 32-char hex MongoDB ID.
        // Response includes a DiscountedPrice field populated by a best-effort gRPC call
        // to AK.Discount; if the gRPC call fails the product is still returned without discount.
        group.MapGet("/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductByIdQuery(id), ct);
            return result is null ? Results.NotFound(new { error = $"Product '{id}' not found" }) : Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetProductById")
        .WithSummary("Get a product by ID");

        // GET /api/v1/products/category/{category} — all products for a top-level category.
        // Equivalent to GET /? category={category} but as a clean URL for SEO-friendly pages.
        group.MapGet("/category/{category}", async (string category, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProductsByCategoryQuery(category), ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("GetProductsByCategory")
        .WithSummary("Get all products by top-level category name (e.g. Men, Women, Kids)");

        // POST /api/v1/products — creates a single product. Admin only.
        // Returns 201 Created with Location header pointing to the new product URL.
        group.MapPost("/", async (CreateProductDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateProductCommand(dto), ct);
            return Results.Created($"/api/v1/products/{result.Id}", result);
        })
        .RequireAuthorization("admin")
        .WithName("CreateProduct")
        .WithSummary("Create a new product (Admin only)");

        // PUT /api/v1/products/{id} — full replacement update for a product. Admin only.
        group.MapPut("/{id}", async (string id, UpdateProductDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateProductCommand(id, dto), ct);
            return Results.Ok(result);
        })
        .RequireAuthorization("admin")
        .WithName("UpdateProduct")
        .WithSummary("Update an existing product (Admin only)");

        // DELETE /api/v1/products/{id} — hard delete from MongoDB. Admin only.
        group.MapDelete("/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new DeleteProductCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization("admin")
        .WithName("DeleteProduct")
        .WithSummary("Delete a product (Admin only)");

        // POST /api/v1/products/bulk-insert — inserts multiple products in one request. Admin only.
        // Used by the database seeder (300 products) to avoid 300 individual HTTP calls.
        group.MapPost("/bulk-insert", async (List<CreateProductDto> products, IMediator mediator, CancellationToken ct) =>
        {
            var count = await mediator.Send(new BulkInsertProductsCommand(products), ct);
            return Results.Ok(new { inserted = count, message = $"Successfully inserted {count} products" });
        })
        .RequireAuthorization("admin")
        .WithName("BulkInsertProducts")
        .WithSummary("Bulk insert multiple products (Admin only)");

        // PUT /api/v1/products/bulk-update — updates multiple products in one request. Admin only.
        // Useful for batch price changes or setting featured flags across a category.
        group.MapPut("/bulk-update", async (List<BulkUpdateProductDto> updates, IMediator mediator, CancellationToken ct) =>
        {
            var count = await mediator.Send(new BulkUpdateProductsCommand(updates), ct);
            return Results.Ok(new { updated = count, message = $"Successfully updated {count} products" });
        })
        .RequireAuthorization("admin")
        .WithName("BulkUpdateProducts")
        .WithSummary("Bulk update multiple products (Admin only)");
    }
}
