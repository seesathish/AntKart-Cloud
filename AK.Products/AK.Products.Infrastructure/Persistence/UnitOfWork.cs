using AK.Products.Application.Interfaces;
using AK.Products.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace AK.Products.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly MongoDbContext _context;
    private bool _disposed;

    public IProductRepository Products { get; }

    public UnitOfWork(MongoDbContext context, IOptions<MongoDbSettings> settings, ResiliencePipelineProvider<string> pipelines)
    {
        _context = context;
        Products = new ProductRepository(context, settings, pipelines);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // MongoDB operations are auto-saved; return 1 to indicate success
        return Task.FromResult(1);
    }

    public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void Dispose()
    {
        if (!_disposed) _disposed = true;
    }
}
