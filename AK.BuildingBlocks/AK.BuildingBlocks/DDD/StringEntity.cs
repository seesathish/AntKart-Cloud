namespace AK.BuildingBlocks.DDD;

// Shared base class for domain entities that use a string primary key.
// Used by: AK.Products (MongoDB — ID is a 32-char hex GUID string via ToString("N")).
//
// ID default: Guid.NewGuid().ToString("N") produces a compact 32-char hex string.
// MongoDB stores this as a BSON string, not an ObjectId.
public abstract class StringEntity
{
    public string Id { get; protected set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void SetUpdatedAt() => UpdatedAt = DateTimeOffset.UtcNow;
}
