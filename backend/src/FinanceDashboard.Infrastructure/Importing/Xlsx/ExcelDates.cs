namespace FinanceDashboard.Infrastructure.Importing.Xlsx;

/// <summary>Excel date serials (cached cell values) to <see cref="DateOnly"/>, in both date systems.</summary>
public static class ExcelDates
{
    private static readonly int Base1900Early = new DateOnly(1899, 12, 31).DayNumber;
    private static readonly int Base1900 = new DateOnly(1899, 12, 30).DayNumber;
    private static readonly int Base1904 = new DateOnly(1904, 1, 1).DayNumber;

    /// <summary>
    /// Integer part of <paramref name="serial"/> as a date (the time of day is dropped). 1900 system: serial 60 is
    /// Excel's phantom 29/02/1900 (rejected), serials below it count from 31/12/1899 and above it from 30/12/1899.
    /// 1904 system: counts from 01/01/1904. Serial ≤ 0 or outside <see cref="DateOnly"/> = <c>invalid_cell</c>
    /// carrying only <paramref name="row"/>.
    /// </summary>
    public static DateOnly FromSerial(decimal serial, bool date1904, int row)
    {
        var whole = decimal.Truncate(serial);
        if (whole <= 0 || whole > DateOnly.MaxValue.DayNumber) throw Invalid(row);
        var days = (int)whole;

        int baseDay;
        if (date1904) baseDay = Base1904;
        else if (days == 60) throw Invalid(row);
        else baseDay = days < 60 ? Base1900Early : Base1900;

        if ((long)baseDay + days > DateOnly.MaxValue.DayNumber) throw Invalid(row);
        return DateOnly.FromDayNumber(baseDay + days);
    }

    private static ImportFormatException Invalid(int row) => new("invalid_cell", row);
}
