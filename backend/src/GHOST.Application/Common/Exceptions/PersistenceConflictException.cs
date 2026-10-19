namespace GHOST.Application.Common.Exceptions;

/// <summary>
/// Indicates that a persistence-level concurrency or uniqueness constraint
/// prevented an operation from being saved.
/// </summary>
public sealed class PersistenceConflictException : Exception
{
    public PersistenceConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
