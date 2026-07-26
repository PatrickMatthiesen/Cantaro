namespace Cantaro.Api.Services;

public class PlatformApiException : Exception
{
    public PlatformApiException(
        string code,
        string message,
        int statusCode,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}

public sealed class PlatformReconnectRequiredException : PlatformApiException
{
    public PlatformReconnectRequiredException(string message, Exception? innerException = null)
        : base("platform_reconnect_required", message, StatusCodes.Status409Conflict, innerException: innerException)
    {
    }
}
