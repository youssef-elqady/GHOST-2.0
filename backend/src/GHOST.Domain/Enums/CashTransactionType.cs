namespace GHOST.Domain.Enums;

public enum CashTransactionType
{
    OpeningBalance = 1,
    SaleCash = 2,
    Expense = 3,
    Withdrawal = 4,
    StockPurchase = 5,
    StockPurchaseReversal = 6,
    HandoverOut = 7,
    HandoverIn = 8,
    Refund = 9,
    Adjustment = 10
}
