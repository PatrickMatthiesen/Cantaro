using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MusicSyncFailureClassifierDatabaseTests
{
    [Fact]
    public void TransientDatabaseException_IsRetryableAndSanitized()
    {
        var result = MusicSyncFailureClassifier.Classify(new TestDatabaseException(isTransient: true,
            "connection to private-db failed with password=hidden"));

        Assert.Equal("sync_database_failed", result.Code);
        Assert.True(result.Retryable);
        Assert.Equal("Cantaro could not save the playlist sync changes. Retry the sync.", result.Message);
        Assert.DoesNotContain("private-db", result.Message);
        Assert.DoesNotContain("password", result.Message);
    }

    [Fact]
    public void TransientDatabaseExceptionWrappedByEfUpdateException_RemainsRetryable()
    {
        var inner = new TestDatabaseException(isTransient: true, "connection timeout");
        var result = MusicSyncFailureClassifier.Classify(new DbUpdateException("Save failed", inner));

        Assert.Equal("sync_database_failed", result.Code);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("connection timeout", result.Message);
    }

    [Fact]
    public void PermanentDatabaseConstraintException_IsNotRetryable()
    {
        var inner = new TestDatabaseException(isTransient: false,
            "violates foreign key constraint playlist_entries_track_id_fkey");
        var result = MusicSyncFailureClassifier.Classify(new DbUpdateException("Save failed", inner));

        Assert.Equal("sync_database_failed", result.Code);
        Assert.False(result.Retryable);
        Assert.DoesNotContain("foreign key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestDatabaseException(bool isTransient, string message) : DbException(message)
    {
        public override bool IsTransient => isTransient;
    }
}
