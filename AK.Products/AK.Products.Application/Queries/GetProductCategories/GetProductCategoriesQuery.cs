using MediatR;

namespace AK.Products.Application.Queries.GetProductCategories;

public sealed record GetProductCategoriesQuery : IRequest<IReadOnlyList<string>>;
