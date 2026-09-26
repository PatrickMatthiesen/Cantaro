using Cantaro.Api.Controllers;
using System.Data.Common;

namespace Cantaro.Api.Services;

public sealed record MusicSyncFailure(string Code, string Message, bool Retryable);

public static class MusicSyncFailureClassifier
{
    public static MusicSyncFailure Classify(Exception exception)
    {
        if (exception is HttpRequestException or OperationCanceledException)
        {
            return new MusicSyncFailure("sync_connection_failed", "The provider request failed or timed out. Retry the sync.", true);
        }
        if (exception is PlatformApiException platformException)
        {
            var retryable = platformException.RetryAfter.HasValue
                || platformException.Code == "unmatched_tracks"
                || platformException.StatusCode == StatusCodes.Status429TooManyRequests
                || platformException.StatusCode >= StatusCodes.Status500InternalServerError;
            return new MusicSyncFailure(platformException.Code, platformException.Message, retryable);
        }

        if (FindDatabaseException(exception) is { } databaseException)
        {
            return new MusicSyncFailure(
                "sync_database_failed",
                "Cantaro could not save the playlist sync changes. Retry the sync.",
                databaseException.IsTransient);
        }

        return new MusicSyncFailure(
            "sync_failed",
            "Playlist could not be synced. Check the server logs for details.",
            Retryable: false);
    }

    private static DbException? FindDatabaseException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbException databaseException)
                return databaseException;

        return null;
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
            result.Error = "Playlist could not be synced. Check the server logs for details.";
            result.Retryable = false;
        }

        return result;
    }
}
