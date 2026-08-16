namespace AK.BuildingBlocks.DDD;

// Abstract base class for value objects — objects whose identity is defined entirely
// by their property values, not by a unique Id.
//
// Contrast with Entity/StringEntity: two entities with the same data are still distinct
// objects; two value objects with the same data ARE equal.
//
// Usage: subclass and implement GetEqualityComponents() by yielding each property
// that participates in equality. Equals(), GetHashCode(), == and != are then derived
// automatically from those components.
//
// Example:
//   public sealed class Money : ValueObject {
//       protected override IEnumerable<object?> GetEqualityComponents() {
//           yield return Amount;
//           yield return Currency;
//       }
//   }
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;
        return GetEqualityComponents()
            .SequenceEqual(((ValueObject)obj).GetEqualityComponents());
    }

    public override int GetHashCode() =>
        GetEqualityComponents()
            .Aggregate(0, (hash, component) =>
                HashCode.Combine(hash, component?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) =>
        !(left == right);
}
