namespace CaseTrack.Application.Exceptions;

public sealed class NotFoundException : Exception
{
    public string ResourceName { get; }
    public object Key { get; }

    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} '{key}' was not found.")
    {
        ResourceName = resourceName;
        Key = key;
    }
}