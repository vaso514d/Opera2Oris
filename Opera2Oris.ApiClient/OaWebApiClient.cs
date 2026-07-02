using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opera2Oris.Entities;

namespace Opera2Oris.ApiClient;

public sealed class OaWebApiClient : IOaWebApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public OaWebApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public OaWebApiClient(Uri baseAddress)
        : this(new HttpClient { BaseAddress = baseAddress })
    {
    }

    public Task<OaLoginResponse> LogInAsync(OaLoginRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OaLoginRequest, OaLoginResponse>(HttpMethod.Post, "api/LogIn", request, cancellationToken);

    public Task<OaMutationResult> CreateTransactionAsync(OaTransactionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OaTransactionRequest, OaMutationResult>(HttpMethod.Post, "api/Transaction", request, cancellationToken);

    public Task<OaMutationResult> UpdateTransactionAsync(OaTransactionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OaTransactionRequest, OaMutationResult>(HttpMethod.Put, "api/Transaction", request, cancellationToken);

    public Task<OaMutationResult> DeleteTransactionAsync(OaTransactionIdRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OaTransactionIdRequest, OaMutationResult>(HttpMethod.Delete, "api/Transaction", request, cancellationToken);

    public Task<JsonDocument> GetTransactionByIdAsync(OaTransactionIdRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OaTransactionIdRequest, JsonDocument>(HttpMethod.Post, "api/GetTransactionByID", request, cancellationToken);

    public Task<JsonDocument> GetEntriesListAsync(OaDateRangeListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OaDateRangeListRequest, JsonDocument>(HttpMethod.Post, "api/EntriesList", request, cancellationToken);

    public async Task<IReadOnlyList<OaMutationResult>> UploadTransactionsAsync(
        IEnumerable<OaTransactionRequest> transactions,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var results = new List<OaMutationResult>();
        foreach (var transaction in transactions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Token = token;
            results.Add(await CreateTransactionAsync(transaction, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public Task<TResponse> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<TRequest, TResponse>(HttpMethod.Post, endpoint, request, cancellationToken);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(method, endpoint)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OaApiException(response.StatusCode, ReadErrors(responseBody), responseBody);
        }

        if (typeof(TResponse) == typeof(JsonDocument))
        {
            return (TResponse)(object)JsonDocument.Parse(responseBody);
        }

        var result = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException($"OA API returned an empty or invalid response for endpoint '{endpoint}'.");
        }

        return result;
    }

    private static IReadOnlyList<OaApiError> ReadErrors(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<OaApiError>>(responseBody, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
