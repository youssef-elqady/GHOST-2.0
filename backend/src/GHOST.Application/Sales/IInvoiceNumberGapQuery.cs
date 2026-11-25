namespace GHOST.Application.Sales;

public interface IInvoiceNumberGapQuery
{
    Task<InvoiceNumberGapReport> GetAsync(CancellationToken cancellationToken = default);
}
