namespace AK.BuildingBlocks.Exceptions;
public class NotFoundException(string name, object key)
    : Exception($"{name} with key '{key}' was not found.");
