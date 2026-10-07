namespace GHOST.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    protected BaseEntity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    protected BaseEntity(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Entity ID cannot be empty.", nameof(id));

        Id = id;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    protected void MarkAsUpdated()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}