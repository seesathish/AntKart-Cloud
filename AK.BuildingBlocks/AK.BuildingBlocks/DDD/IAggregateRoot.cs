namespace AK.BuildingBlocks.DDD;

// Marker interface identifying an entity as an Aggregate Root.
// An Aggregate Root is the single entry point for all changes within an aggregate boundary.
// External code may only hold references to aggregate roots, never to internal entities.
public interface IAggregateRoot { }
