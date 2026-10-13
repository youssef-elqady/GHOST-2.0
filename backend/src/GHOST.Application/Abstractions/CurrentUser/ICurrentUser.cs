
namespace GHOST.Application.Abstractions.CurrentUser;

public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    bool IsAdmin { get; }

    bool IsStaff { get; }
}