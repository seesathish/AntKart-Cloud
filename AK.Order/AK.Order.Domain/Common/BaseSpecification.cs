using System.Linq.Expressions;

namespace AK.Order.Domain.Common;

public abstract class BaseSpecification<T> : ISpecification<T>
{
    protected BaseSpecification(Expression<Func<T, bool>> criteria) => Criteria = criteria;

    public Expression<Func<T, bool>> Criteria { get; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public int? Skip { get; private set; }
    public int? Take { get; private set; }

    protected void AddInclude(Expression<Func<T, object>> expr) => Includes.Add(expr);
    protected void ApplyOrderBy(Expression<Func<T, object>> expr) => OrderBy = expr;
    protected void ApplyOrderByDescending(Expression<Func<T, object>> expr) => OrderByDescending = expr;
    protected void ApplyPaging(int skip, int take) { Skip = skip; Take = take; }
}
