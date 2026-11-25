using System.Text.RegularExpressions;

namespace GHOST.Application.Sales;

/// <summary>One number below the highest invoice number that no invoice uses. It is NOT an invoice and NOT a sale.</summary>
public sealed record InvoiceNumberGap(long Number, bool ReasonKnown, string? Reason);

public sealed record InvoiceNumberGapReport(
    long? LowestInvoiceNumber,
    long? HighestInvoiceNumber,
    int InvoiceCount,
    IReadOnlyList<InvoiceNumberGap> Gaps,
    int GapsWithKnownReason,
    int GapsWithUnknownReason,
    string Note);

/// <summary>
/// Finds numbers that no invoice uses. Gaps are normal (a sequence is not rolled back) and are never sales.
/// Only numbers between the lowest and highest invoice number are listed: a number above the highest invoice may simply
/// not have been issued yet, so it cannot be called a gap. "Known reason" means a rejection note was recorded for that
/// exact number; every other gap has no record (for example a crash or a restart), and is reported as unknown, never as a
/// confirmed reservation.
/// </summary>
public static class InvoiceNumberGaps
{
    public const string UnusedNumberAction = "Sale.InvoiceNumberUnused";
    public const string UnusedNumberEntityType = "InvoiceNumber";

    private static readonly Regex NumberPattern = new(@"^GS-\d{4}-(\d{1,18})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The sequence part of GS-2026-00007, or null when the text is not in that format.</summary>
    public static long? TryParse(string? invoiceNumber)
    {
        if (invoiceNumber is null)
            return null;

        var match = NumberPattern.Match(invoiceNumber.Trim());
        return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    public static InvoiceNumberGapReport Analyze(
        IEnumerable<string> invoiceNumbers,
        IEnumerable<(string InvoiceNumber, string Reason)> unusedNotes)
    {
        var used = new HashSet<long>();
        foreach (var text in invoiceNumbers)
            if (TryParse(text) is { } n)
                used.Add(n);

        var reasons = new Dictionary<long, string>();
        foreach (var (text, reason) in unusedNotes)
            if (TryParse(text) is { } n && !reasons.ContainsKey(n))
                reasons[n] = reason;

        const string note = "These numbers are not invoices and are not sales. A gap with a known reason has a recorded note for that number. "
            + "A gap with an unknown reason has no record (for example a crash). Numbers above the highest invoice are not listed.";

        if (used.Count == 0)
            return new InvoiceNumberGapReport(null, null, 0, Array.Empty<InvoiceNumberGap>(), 0, 0, note);

        var low = used.Min();
        var high = used.Max();
        var gaps = new List<InvoiceNumberGap>();
        for (var n = low; n <= high; n++)
        {
            if (used.Contains(n))
                continue;

            gaps.Add(reasons.TryGetValue(n, out var reason)
                ? new InvoiceNumberGap(n, true, reason)
                : new InvoiceNumberGap(n, false, null));
        }

        var known = gaps.Count(g => g.ReasonKnown);
        return new InvoiceNumberGapReport(low, high, used.Count, gaps, known, gaps.Count - known, note);
    }
}
