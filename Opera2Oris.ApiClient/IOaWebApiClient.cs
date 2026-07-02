using System.Text.Json;
using Opera2Oris.Entities;

namespace Opera2Oris.ApiClient;

public interface IOaWebApiClient
{
    Task<OaLoginResponse> LogInAsync(OaLoginRequest request, CancellationToken cancellationToken = default);

    Task<OaMutationResult> CreateTransactionAsync(OaTransactionRequest request, CancellationToken cancellationToken = default);

    Task<OaMutationResult> UpdateTransactionAsync(OaTransactionRequest request, CancellationToken cancellationToken = default);

    Task<OaMutationResult> DeleteTransactionAsync(OaTransactionIdRequest request, CancellationToken cancellationToken = default);

    Task<JsonDocument> GetTransactionByIdAsync(OaTransactionIdRequest request, CancellationToken cancellationToken = default);

    Task<JsonDocument> GetEntriesListAsync(OaDateRangeListRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OaMutationResult>> UploadTransactionsAsync(
        IEnumerable<OaTransactionRequest> transactions,
        string token,
        CancellationToken cancellationToken = default);
}
