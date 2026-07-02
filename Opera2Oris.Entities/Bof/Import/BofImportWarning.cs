namespace Opera2Oris.Entities;

public sealed record BofImportWarning(string? FilePath, long? LineNumber, string Message)
{
    public override string ToString()
    {
        var location = FilePath is null ? "import" : System.IO.Path.GetFileName(FilePath);

        if (LineNumber is null)
        {
            return $"{location}: {Message}";
        }

        return $"{location}:{LineNumber}: {Message}";
    }
}
