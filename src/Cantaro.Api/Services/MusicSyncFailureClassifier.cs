using Cantaro.Api.Controllers;

namespace Cantaro.Api.Services;

public sealed record MusicSyncFailure(string Code, string Message, bool Retryable);

public static class MusicSyncFailureClassifier
{
    public static MusicSyncFailure Classify(Exception exception)
    {
        if (exception is PlatformApiException platformException)
        {
            var retryable = platformException.RetryAfter.HasValue
                || platformException.StatusCode == StatusCodes.Status429TooManyRequests
                || platformException.StatusCode >= StatusCodes.Status500InternalServerError;
            return new MusicSyncFailure(platformException.Code, platformException.Message, retryable);
        }

        return new MusicSyncFailure(
            "sync_failed",
            "Playlist could not be imported. Check the server logs for details.",
            Retryable: false);
    }

    public static BatchSyncResult SanitizeLegacy(BatchSyncResult result)
    {
        if (result.Success)
        {
            result.Error = null;
            result.ErrorCode = null;
            result.Retryable = false;
            return result;
        }

        if (string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            result.ErrorCode = "sync_failed";
            result.Error = "Playlist could not be imported. Check the server logs for details.";
            result.Retryable = false;
        }

        return result;
    }
}
