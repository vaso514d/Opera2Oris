using Opera2Oris.Entities;

namespace Opera2Oris.Middlewear.Outbox;

internal sealed class OaTransactionOutboxRecord
{
    public string Id { get; set; } = string.Empty;

    public OaTransactionOutboxStatus Status { get; set; } = OaTransactionOutboxStatus.Pending;

    public string? SourceFilePath { get; set; }

    public long? SourceLineNumber { get; set; }

    public string? TransactionDocumentNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public string? LastErrorType { get; set; }

    public long? OaTransactionsId { get; set; }

    public OaTransactionRequest Payload { get; set; } = new();
}
