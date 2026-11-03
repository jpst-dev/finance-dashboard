using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Infrastructure.Importing.Xlsx;

namespace FinanceDashboard.Tests.Importing;

public sealed class ExcelDatesTests
{
    [Theory]
    [InlineData("46000", false, 2025, 12, 9)]
    [InlineData("1", false, 1900, 1, 1)]
    [InlineData("59", false, 1900, 2, 28)]
    [InlineData("61", false, 1900, 3, 1)]
    [InlineData("44538", true, 2025, 12, 9)] // 1904 system = 1900 system − 1462
    [InlineData("46000.75", false, 2025, 12, 9)] // time of day is dropped
    [InlineData("1", true, 1904, 1, 2)]
    public void Converts_serial_to_date(string serial, bool date1904, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), ExcelDates.FromSerial(decimal.Parse(serial, System.Globalization.CultureInfo.InvariantCulture), date1904, 7));

    [Theory]
    [InlineData("60", false)] // 29/02/1900: Excel's phantom leap day
    [InlineData("0", false)]
    [InlineData("0", true)]
    [InlineData("-5", false)]
    [InlineData("0.5", false)]
    [InlineData("3000000", false)] // beyond DateOnly.MaxValue
    [InlineData("79228162514264337593543950335", true)]
    public void Invalid_serial_is_invalid_cell_with_row(string serial, bool date1904)
    {
        var e = Assert.Throws<ImportFormatException>(() =>
            ExcelDates.FromSerial(decimal.Parse(serial, System.Globalization.CultureInfo.InvariantCulture), date1904, 42));
        Assert.Equal("invalid_cell", e.Kind);
        Assert.Equal(42, e.Line);
    }
}
