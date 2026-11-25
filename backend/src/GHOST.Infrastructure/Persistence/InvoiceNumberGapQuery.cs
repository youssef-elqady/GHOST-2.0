using System.Text.Json;
using GHOST.Application.Sales;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence;

/// <summary>Read only. Loads invoice numbers and the recorded "number not used" notes, then compares them.</summary>
public sealed class InvoiceNumberGapQuery : IInvoiceNumberGapQuery
{
    private readonly GHOSTDbContext _db;

    public InvoiceNumberGapQuery(GHOSTDbContext db) => _db = db;

    public async Task<InvoiceNumberGapReport> GetAsync(CancellationToken cancellationToken = default)
    {
        var numbers = await _db.Sales.AsNoTracking()
            .Select(s => s.InvoiceNumber)
            .ToListAsync(cancellationToken);

        var notes = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == InvoiceNumberGaps.UnusedNumberAction && a.EntityId != null)
            .Select(a => new { a.EntityId, a.NewValues })
            .ToListAsync(cancellationToken);

        return InvoiceNumberGaps.Analyze(
            numbers,
            notes.Select(n => (n.EntityId!, ReadReason(n.NewValues))));
    }

    private static string ReadReason(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "Reason not recorded.";

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                ? reason.GetString() ?? "Reason not recorded."
                : "Reason not recorded.";
        }
        catch (JsonException)
        {
            return "Reason not recorded.";
        }
    }
}
