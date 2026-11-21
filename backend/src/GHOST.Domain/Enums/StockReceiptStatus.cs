namespace GHOST.Domain.Enums;

public enum StockReceiptStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

/// <summary>What happens to the money when an Admin rejects a receipt (FR-35).</summary>
public enum ReceiptRejectionOutcome
{
    /// <summary>The cash goes back into a currently open drawer.</summary>
    ReverseCash = 1,
    /// <summary>No cash comes back; the amount is a shortfall charged to the staff member.</summary>
    ChargeToStaff = 2
}
