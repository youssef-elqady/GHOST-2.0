namespace GHOST.Domain.Enums;

/// <summary>Fixed reason list from SRS BD-03 (rule 2).</summary>
public enum SessionCancelReason
{
    ChangedMind = 1,
    WrongDevice = 2,
    MovedSeat = 3,
    OperatorError = 4,
    Other = 5
}
