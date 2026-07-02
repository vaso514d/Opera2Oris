using System.Text.Json;
using System.Text.Json.Serialization;
using Opera2Oris.Entities;

namespace Opera2Oris.Middlewear;

internal sealed class OaTransactionPayloadDumper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly PayloadDumpSettings _settings;

    public OaTransactionPayloadDumper(PayloadDumpSettings settings)
    {
        _settings = settings;
    }

    public async Task<int> DumpAsync(IReadOnlyList<OaTransactionRequest> transactions, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled || transactions.Count == 0)
        {
            return 0;
        }

        Directory.CreateDirectory(_settings.Directory);

        for (var index = 0; index < transactions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteTransactionAsync(transactions[index], index + 1, cancellationToken).ConfigureAwait(false);
        }

        return transactions.Count;
    }

    private async Task WriteTransactionAsync(
        OaTransactionRequest transaction,
        int index,
        CancellationToken cancellationToken)
    {
        transaction.Token = null;
        var path = Path.Combine(_settings.Directory, BuildFileName(transaction, index));
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, transaction, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static string BuildFileName(OaTransactionRequest transaction, int index)
    {
        var sourceName = string.IsNullOrWhiteSpace(transaction.SourceFilePath)
            ? "transaction"
            : Path.GetFileName(transaction.SourceFilePath);
        var sourceLine = transaction.SourceLineNumber is null
            ? $"item{index:000000}"
            : $"line{transaction.SourceLineNumber.Value:000000}";
        var documentNumber = string.IsNullOrWhiteSpace(transaction.TransactionDocumentNumber)
            ? "no-document"
            : transaction.TransactionDocumentNumber;

        return $"{SanitizeFileName(sourceName)}_{sourceLine}_{SanitizeFileName(documentNumber)}.json";
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var characters = value
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray();

        return new string(characters);
    }
}
