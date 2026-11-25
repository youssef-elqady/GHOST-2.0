using GHOST.Application.Sales;
using GHOST.Infrastructure.Persistence;

namespace GHOST.Tests.Sales;

public sealed class InvoiceNumberGapsTests
{
    [Theory]
    [InlineData("GS-2026-00007", 7L)]
    [InlineData("GS-2027-00001", 1L)]
    [InlineData("  GS-2026-123456  ", 123456L)]
    public void TryParse_ReadsTheSequencePart(string text, long expected)
        => Assert.Equal(expected, InvoiceNumberGaps.TryParse(text));

    [Theory]
    [InlineData("")]
    [InlineData("INV-1")]
    [InlineData("GS-26-00001")]
    [InlineData("GS-2026-")]
    [InlineData("GS-2026-00001-X")]
    [InlineData("GS-2026-9999999999999999999999")]
    public void TryParse_RejectsOtherFormats(string text)
        => Assert.Null(InvoiceNumberGaps.TryParse(text));

    [Fact]
    public void NoInvoices_NoGaps()
    {
        var report = InvoiceNumberGaps.Analyze(Array.Empty<string>(), new[] { ("GS-2026-00001", "x") });
        Assert.Empty(report.Gaps);
        Assert.Null(report.HighestInvoiceNumber);
    }

    [Fact]
    public void ContinuousNumbers_NoGaps()
    {
        var report = InvoiceNumberGaps.Analyze(new[] { "GS-2026-00001", "GS-2026-00002", "GS-2026-00003" },
            Array.Empty<(string, string)>());
        Assert.Empty(report.Gaps);
        Assert.Equal(3, report.InvoiceCount);
    }

    [Fact]
    public void Gaps_AreSplit_IntoKnownReason_AndUnknownReason()
    {
        var report = InvoiceNumberGaps.Analyze(
            new[] { "GS-2026-00001", "GS-2026-00004", "GS-2026-00007" },
            new[] { ("GS-2026-00002", "Insufficient stock."), ("GS-2026-00005", "conflict") });

        Assert.Equal(new long[] { 2, 3, 5, 6 }, report.Gaps.Select(g => g.Number).ToArray());
        Assert.Equal(2, report.GapsWithKnownReason);
        Assert.Equal(2, report.GapsWithUnknownReason);
        Assert.True(report.Gaps.Single(g => g.Number == 2).ReasonKnown);
        Assert.Equal("Insufficient stock.", report.Gaps.Single(g => g.Number == 2).Reason);
        Assert.False(report.Gaps.Single(g => g.Number == 3).ReasonKnown);
        Assert.Null(report.Gaps.Single(g => g.Number == 3).Reason);
    }

    [Fact]
    public void NumbersAboveTheHighestInvoice_AreNotListed_AndNotesForUsedNumbersAreIgnored()
    {
        var report = InvoiceNumberGaps.Analyze(
            new[] { "GS-2026-00001", "GS-2026-00002" },
            new[] { ("GS-2026-00009", "tail"), ("GS-2026-00002", "stale note for a used number") });

        Assert.Empty(report.Gaps);
        Assert.Equal(2, report.HighestInvoiceNumber);
    }

    [Fact]
    public void ReportNote_SaysGapsAreNotInvoicesOrSales()
    {
        var report = InvoiceNumberGaps.Analyze(new[] { "GS-2026-00001" }, Array.Empty<(string, string)>());
        Assert.Contains("not invoices", report.Note);
        Assert.Contains("not sales", report.Note);
    }
}

public sealed class SqlSequenceTests
{
    [Theory]
    [InlineData("InvoiceNumbers", "SELECT NEXT VALUE FOR [dbo].[InvoiceNumbers]")]
    [InlineData("InternalBarcodes", "SELECT NEXT VALUE FOR [dbo].[InternalBarcodes]")]
    public void Sql_IsAPlainSelect_WithNoDerivedTableWrapper(string name, string expected)
    {
        var sql = SqlSequence.BuildNextValueSql(name);
        Assert.Equal(expected, sql);
        Assert.DoesNotContain("FROM (", sql);
        Assert.DoesNotContain("TOP", sql);
    }

    [Theory]
    [InlineData("Other")]
    [InlineData("InvoiceNumbers]; DROP TABLE Sales;--")]
    [InlineData("")]
    public void UnknownSequenceName_IsRefused(string name)
        => Assert.Throws<ArgumentException>(() => SqlSequence.BuildNextValueSql(name));
}
