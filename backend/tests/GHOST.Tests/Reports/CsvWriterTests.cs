using System.Text;
using GHOST.API.Reports;

namespace GHOST.Tests.Reports;

public sealed class CsvWriterTests
{
    [Fact]
    public void Cell_QuotesCommasQuotesAndNewlines()
    {
        Assert.Equal("\"a,b\"", CsvWriter.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvWriter.Cell("say \"hi\""));
        Assert.Equal("\"x\ny\"", CsvWriter.Cell("x\ny"));
    }

    [Fact]
    public void Cell_NeutralisesFormulaText()
    {
        Assert.Equal("'=SUM(A1)", CsvWriter.Cell("=SUM(A1)"));
        Assert.Equal("'@cmd", CsvWriter.Cell("@cmd"));
        Assert.Equal("-5", CsvWriter.Cell(-5m));
    }

    [Fact]
    public void Build_StartsWithBom_AndKeepsArabic()
    {
        var bytes = CsvWriter.Build(new[] { "Name" }, new[] { (IReadOnlyList<object?>)new object?[] { "كولا" } });

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Contains("كولا", Encoding.UTF8.GetString(bytes));
    }
}
