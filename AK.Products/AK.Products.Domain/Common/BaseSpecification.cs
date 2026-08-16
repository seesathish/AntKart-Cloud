using System.Linq.Expressions;
namespace AK.Products.Domain.Common;

public abstract class BaseSpecification<T> : ISpecification<T>
{
    protected BaseSpecification(Expression<Func<T, bool>> criteria) => Criteria = criteria;

    public Expression<Func<T, bool>> Criteria { get; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public int? Skip { get; private set; }
    public int? Take { get; private set; }

    protected void AddInclude(Expression<Func<T, object>> includeExpr) => Includes.Add(includeExpr);
    protected void ApplyOrderBy(Expression<Func<T, object>> orderByExpr) => OrderBy = orderByExpr;
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescExpr) => OrderByDescending = orderByDescExpr;
    protected void ApplyPaging(int skip, int take) { Skip = skip; Take = take; }
}
