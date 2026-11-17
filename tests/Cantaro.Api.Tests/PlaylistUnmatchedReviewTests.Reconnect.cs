using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [PostgresFact]
    public async Task SpotifySearch_ContinuesWhenOptionalYouTubeEnrichmentRequiresReconnect()
    {
        await using var scope = await Scope.CreateAsync("spotify", postgres: true);
        var credentialAttempts = 0;
        var youtube = new YouTubeService(new ConfigurationBuilder().Build(), scope.Db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance,
            (_, _) =>
            {
                credentialAttempts++;
                throw new TokenResponseException(new TokenErrorResponse { Error = "invalid_grant" });
            });
        using var handler = new CanonicalInputHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var review = await scope.SearchReviewAsync(http, youtube);
        var metadata = TrackObservationParser.ReadMetadata(ObservationInput.Read(
            scope.Entry.TrackObservation, scope.UserId, DateTimeOffset.UtcNow)!)!;
        metadata.Description = null;
        var original = scope.Entry.TrackObservation!.PrivateInputJson = JsonSerializer.Serialize(metadata);
        var account = new ConnectedServiceAccount
        {
            UserId = scope.UserId, Service = "youtube", ExternalAccountId = "youtube-owner",
            EncryptedRefreshToken = "refresh", ConnectionState = "connected"
        };
        scope.Db.ConnectedServiceAccounts.Add(account);
        await scope.Db.SaveChangesAsync();

        // Exercise the initial rejection and the subsequent persisted reconnect state.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
                scope.Entry.Id, "Everything Goes On Porter Robinson", default);
            Assert.Equal("chosen-id", Assert.Single(result.Candidates).ExternalId);
            Assert.Equal(original, (await scope.Db.TrackObservations.AsNoTracking()
                .SingleAsync(item => item.Id == scope.Entry.TrackObservationId)).PrivateInputJson);
            // The next HTTP request starts with a fresh scoped tracker.
            scope.Db.ChangeTracker.Clear();
        }
        Assert.Equal(1, credentialAttempts);
        Assert.Equal("reconnect_required", (await scope.Db.ConnectedServiceAccounts.AsNoTracking()
            .SingleAsync(item => item.Id == account.Id)).ConnectionState);
        Assert.Equal("connected", (await scope.Db.ConnectedServiceAccounts.AsNoTracking()
            .SingleAsync(item => item.Service == "spotify")).ConnectionState);
    }
}
