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
    [PostgresTheory]
    [InlineData("invalid_grant", null, true)]
    [InlineData("temporarily_unavailable", null, false)]
    [InlineData("server_error", null, false)]
    [InlineData("other", 429, false)]
    [InlineData("other", 503, false)]
    public async Task SpotifySearch_ContinuesWhenOptionalYouTubeAuthorizationIsUnavailable(
        string error, int? statusCode, bool requiresReconnect)
    {
        await using var scope = await Scope.CreateAsync("spotify", postgres: true);
        var credentialAttempts = 0;
        var youtube = new YouTubeService(new ConfigurationBuilder().Build(), scope.Db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance,
            (_, _) =>
            {
                credentialAttempts++;
                throw new TokenResponseException(new TokenErrorResponse { Error = error },
                    statusCode is { } status ? (System.Net.HttpStatusCode)status : null);
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

        // Exercise both the initial failure and the next request's account state.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // Supply the owned playlist on each request so optional enrichment remains eligible.
            await scope.Db.Playlists.SingleAsync(item => item.Id == scope.Playlist.Id);
            var result = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
                scope.Entry.Id, "Everything Goes On Porter Robinson", default);
            Assert.Equal("chosen-id", Assert.Single(result.Candidates).ExternalId);
            Assert.Equal(original, (await scope.Db.TrackObservations.AsNoTracking()
                .SingleAsync(item => item.Id == scope.Entry.TrackObservationId)).PrivateInputJson);
            // The next HTTP request starts with a fresh scoped tracker.
            scope.Db.ChangeTracker.Clear();
        }
        Assert.Equal(requiresReconnect ? 1 : 2, credentialAttempts);
        Assert.Equal(requiresReconnect ? "reconnect_required" : "connected", (await scope.Db.ConnectedServiceAccounts.AsNoTracking()
            .SingleAsync(item => item.Id == account.Id)).ConnectionState);
        Assert.Equal("connected", (await scope.Db.ConnectedServiceAccounts.AsNoTracking()
            .SingleAsync(item => item.Service == "spotify")).ConnectionState);
    }
}
