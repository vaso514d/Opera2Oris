using System.Globalization;

namespace Opera2Oris.Entities;

public sealed record BofFieldValue(BofColumnDefinition Column, string? RawValue)
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "dd-MMM-yy",
        "yyyy-MM-dd HH:mm:ss"
    ];

    private static readonly string[] TimeFormats =
    [
        "HH:mm:ss",
        "H:mm:ss"
    ];

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "dd-MMM-yy"
    ];

    public bool HasValue => !string.IsNullOrEmpty(RawValue);

    public string? AsString() => RawValue;

    public long? AsInt64()
    {
        if (string.IsNullOrWhiteSpace(RawValue))
        {
            return null;
        }

        var value = NormalizeNumber(RawValue);
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) &&
            decimal.Truncate(number) == number)
        {
            return (long)number;
        }

        return null;
    }

    public decimal? AsDecimal()
    {
        if (string.IsNullOrWhiteSpace(RawValue))
        {
            return null;
        }

        var value = NormalizeNumber(RawValue);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    public DateOnly? AsDate()
    {
        if (string.IsNullOrWhiteSpace(RawValue))
        {
            return null;
        }

        var value = RawValue.Trim();
        if (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return DateTime.TryParseExact(value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
            ? DateOnly.FromDateTime(dateTime)
            : null;
    }

    public TimeOnly? AsTime()
    {
        if (string.IsNullOrWhiteSpace(RawValue))
        {
            return null;
        }

        return TimeOnly.TryParseExact(RawValue.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    public DateTime? AsDateTime()
    {
        if (string.IsNullOrWhiteSpace(RawValue))
        {
            return null;
        }

        return DateTime.TryParseExact(RawValue.Trim(), DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
            ? dateTime
            : null;
    }

    private static string NormalizeNumber(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            return $"0{trimmed}";
        }

        if (trimmed.StartsWith("-.", StringComparison.Ordinal) || trimmed.StartsWith("+.", StringComparison.Ordinal))
        {
            return $"{trimmed[0]}0{trimmed[1..]}";
        }

        return trimmed;
    }
}
