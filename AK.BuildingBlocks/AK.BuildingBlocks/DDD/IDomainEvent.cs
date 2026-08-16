namespace AK.BuildingBlocks.DDD;

// Marker interface for domain events.
// Implement this on any record that represents "something meaningful happened" inside an entity.
// The UnitOfWork dispatches these after SaveChangesAsync so events are only published for
// changes that actually persisted to the database.
public interface IDomainEvent { }
