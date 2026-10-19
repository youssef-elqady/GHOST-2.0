using GHOST.Domain.Enums;

namespace GHOST.Application.Sessions.StartSession;

public sealed record StartSessionCommand(
    Guid ShiftId,
    Guid DeviceId,
    PlayType PlayType);
