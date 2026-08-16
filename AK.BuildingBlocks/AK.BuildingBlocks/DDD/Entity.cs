namespace AK.BuildingBlocks.DDD;

// Shared base class for all domain entities that use a Guid primary key.
// Used by: AK.Order, AK.Payments, AK.Notification.
//
// Domain events: when something meaningful happens inside an entity (order placed, payment failed),
// the entity records it as a domain event via AddDomainEvent(). The UnitOfWork dispatches these
// AFTER SaveChangesAsync, ensuring events are only published for persisted changes. Call
// ClearDomainEvents() after dispatch to prevent replaying on the next save cycle.
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void SetUpdatedAt() => UpdatedAt = DateTimeOffset.UtcNow;
}
