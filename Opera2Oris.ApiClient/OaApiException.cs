using System.Net;
using Opera2Oris.Entities;

namespace Opera2Oris.ApiClient;

public sealed class OaApiException : Exception
{
    public OaApiException(HttpStatusCode statusCode, IReadOnlyList<OaApiError> errors, string? responseBody)
        : base(BuildMessage(statusCode, errors, responseBody))
    {
        StatusCode = statusCode;
        Errors = errors;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<OaApiError> Errors { get; }

    public string? ResponseBody { get; }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<OaApiError> errors, string? responseBody)
    {
        if (errors.Count > 0)
        {
            return $"OA API returned {(int)statusCode}: {errors[0]}";
        }

        return $"OA API returned {(int)statusCode}: {responseBody ?? statusCode.ToString()}";
    }
}
