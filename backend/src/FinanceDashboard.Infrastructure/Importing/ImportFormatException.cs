namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Invalid import file. The message carries only the kind and line number, never file content.</summary>
public sealed class ImportFormatException(string kind, int? line) : Exception(BuildMessage(kind, line))
{
    public string Kind { get; } = kind;
    public int? Line { get; } = line;

    private static string BuildMessage(string kind, int? line) =>
        line is null ? $"Invalid import file: {kind}" : $"Invalid import file: {kind} (line {line})";
}
