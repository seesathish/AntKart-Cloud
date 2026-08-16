namespace AK.BuildingBlocks.Exceptions;
public class ValidationException(IEnumerable<string> errors)
    : Exception("One or more validation failures occurred.")
{
    public IReadOnlyList<string> Errors { get; } = errors.ToList();
}
