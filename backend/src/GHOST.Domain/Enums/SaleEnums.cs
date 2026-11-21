namespace GHOST.Domain.Enums;

public enum SaleStatus
{
    Draft = 1,
    Paid = 2,
    Voided = 3,
    Refunded = 4
}

public enum SaleItemType
{
    Product = 1,
    Gaming = 2
}

public enum DiscountType
{
    None = 0,
    Fixed = 1,
    Percent = 2
}

public enum PaymentMethod
{
    Cash = 1
}
