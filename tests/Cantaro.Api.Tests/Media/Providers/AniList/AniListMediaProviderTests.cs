using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class AniListMediaProviderTests
{
    [Fact]
    public async Task ApiClient_PreservesRetryAfterForRateLimitResponses()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(12));
        var client = new AniListApiClient(
            new HttpClient(new SingleResponseHandler(response)),
            Options.Create(new AniListOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret"
            }),
            new AniListRequestGate(TimeProvider.System, TimeSpan.Zero),
            NullLogger<AniListApiClient>.Instance);

        var exception = await Assert.ThrowsAsync<AniListRequestException>(() =>
            client.SendGraphQlAsync<object>("token", "query { Viewer { id } }", null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(12), exception.RetryAfter);
    }

    [Fact]
    public async Task GetRelationGraphAsync_FollowsStructuralFranchiseRelationsAcrossFormats()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(306, "relations@example.com");
        var now = DateTime.UtcNow;
        var dataProtectionProvider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 906,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "306",
            DisplayName = "Relation Tester",
            EncryptedRefreshToken = CreateEncryptedToken(dataProtectionProvider, "access-token"),
            TokenExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        var handler = new QueueHttpMessageHandler(
            """
            {"data":{"Page":{"media":[{
              "id":200,"type":"ANIME","format":"TV","episodes":12,
              "siteUrl":"https://anilist.co/anime/200","title":{"english":"Example Season 2"},
              "relations":{"edges":[
                {"id":1,"relationType":"PREQUEL","node":{"id":100,"type":"ANIME","format":"TV","episodes":12,"title":{"english":"Example"}}},
                {"id":2,"relationType":"SIDE_STORY","node":{"id":300,"type":"ANIME","format":"OVA","episodes":1,"title":{"english":"Example OVA"}}}
              ]}
            }]}}}
            """,
            """
            {"data":{"Page":{"media":[{
              "id":100,"type":"ANIME","format":"TV","episodes":12,
              "siteUrl":"https://anilist.co/anime/100","title":{"english":"Example"},
              "relations":{"edges":[
                {"id":3,"relationType":"SEQUEL","node":{"id":200,"type":"ANIME","format":"TV","episodes":12,"title":{"english":"Example Season 2"}}}
              ]}
            },{
              "id":300,"type":"ANIME","format":"OVA","episodes":1,
              "siteUrl":"https://anilist.co/anime/300","title":{"english":"Example OVA"},
              "relations":{"edges":[
                {"id":4,"relationType":"PARENT","node":{"id":200,"type":"ANIME","format":"TV","episodes":12,"title":{"english":"Example Season 2"}}}
              ]}
            }]}}}
            """);
        var provider = CreateProvider(dbContext, handler, dataProtectionProvider);

        var graph = await provider.GetRelationGraphAsync(user.Id, "200", CancellationToken.None);

        Assert.True(graph.IsComplete);
        Assert.Equal(["100", "200", "300"], graph.RefreshedProviderMediaIds);
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Contains(graph.Nodes, node => node.ProviderMediaId == "100" && node.Format == MediaFormats.Tv);
        Assert.Contains(graph.Nodes, node => node.ProviderMediaId == "300" && node.Format == MediaFormats.Ova);
        Assert.Contains(graph.Edges, edge => edge.MediaProviderMediaId == "200"
            && edge.RelatedProviderMediaId == "100"
            && edge.RelationType == MediaRelationTypes.Prequel);
        Assert.Contains(graph.Edges, edge => edge.MediaProviderMediaId == "200"
            && edge.RelatedProviderMediaId == "300"
            && edge.RelationType == MediaRelationTypes.SideStory);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.All(handler.RequestBodies, body => Assert.Contains("relationType(version: 2)", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetTitleDetailsAsync_NormalizesStreamingAvailabilityLinks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(302, "availability@example.com");
        var now = DateTime.UtcNow;
        var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 902,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "302",
            DisplayName = "Availability Tester",
            EncryptedRefreshToken = CreateEncryptedToken(dataProtectionProvider, "access-token"),
            TokenExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        const string graphQlResponse = """
                        {
                            "data": {
                                "Media": {
                                    "id": 140960,
                                    "type": "ANIME",
                                    "status": "RELEASING",
                                    "siteUrl": "https://anilist.co/anime/140960/Spy-x-Family",
                                    "description": "A spy starts a family.",
                                    "episodes": 25,
                                    "bannerImage": "https://example.test/banner.jpg",
                                    "startDate": { "year": 2022 },
                                    "title": {
                                        "romaji": "Spy x Family",
                                        "english": "Spy x Family",
                                        "native": "SPY x FAMILY"
                                    },
                                    "synonyms": ["SPY×FAMILY", "Spy Family"],
                                    "coverImage": {
                                        "extraLarge": "https://example.test/poster-extra-large.jpg",
                                        "medium": "https://example.test/poster-medium.jpg",
                                        "large": "https://example.test/poster-large.jpg"
                                    },
                                    "nextAiringEpisode": {
                                        "episode": 13,
                                        "airingAt": 1714348800
                                    },
                                    "externalLinks": [
                                        {
                                            "url": "https://www.crunchyroll.com/series/G4PH0WXVJ",
                                            "site": "Crunchyroll",
                                            "type": "STREAMING",
                                            "language": "en",
                                            "icon": "https://example.test/crunchyroll.png"
                                        },
                                        {
                                            "url": "https://myanimelist.net/anime/50265",
                                            "site": "MyAnimeList",
                                            "type": "INFO",
                                            "language": "en",
                                            "icon": null
                                        }
                                    ],
                                    "streamingEpisodes": [
                                        {
                                            "title": "Operation Strix",
                                            "url": "https://www.crunchyroll.com/watch/G50UZK6V7",
                                            "site": "Crunchyroll"
                                        },
                                        {
                                            "title": "Family Outing",
                                            "url": "https://www.netflix.com/watch/81511410",
                                            "site": "Netflix"
                                        }
                                    ],
                                    "characters": {
                                        "edges": [
                                            {
                                                "role": "MAIN",
                                                "node": {
                                                    "id": 170732,
                                                    "siteUrl": "https://anilist.co/character/170732/Anya-Forger",
                                                    "name": { "full": "Anya Forger", "native": "アーニャ・フォージャー", "userPreferred": "Anya Forger" },
                                                    "image": { "large": "https://example.test/anya-large.jpg", "medium": "https://example.test/anya.jpg" }
                                                }
                                            },
                                            {
                                                "role": "SUPPORTING",
                                                "node": {
                                                    "id": 170733,
                                                    "siteUrl": "https://anilist.co/character/170733/Becky-Blackbell",
                                                    "name": { "full": "Becky Blackbell", "native": null, "userPreferred": null },
                                                    "image": { "large": null, "medium": "https://example.test/becky.jpg" }
                                                }
                                            }
                                        ]
                                    }
                                }
                            }
                        }
                        """;

        var handler = new StubHttpMessageHandler(graphQlResponse);
        var provider = CreateProvider(dbContext, handler, dataProtectionProvider);

        var details = await provider.GetTitleDetailsAsync(user.Id, "140960", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(["SPY×FAMILY", "Spy Family"], details.Synonyms);
        Assert.Contains("synonyms", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Equal("https://example.test/poster-extra-large.jpg", details.PosterUrl);
        var availabilityLinks = Assert.IsAssignableFrom<IReadOnlyList<MediaProviderAvailabilityLink>>(details.AvailabilityLinks);
        Assert.Collection(
                availabilityLinks,
                crunchyroll =>
                {
                    Assert.Equal("crunchyroll", crunchyroll.ServiceId);
                    Assert.Equal("Crunchyroll", crunchyroll.DisplayName);
                    Assert.Equal("streaming", crunchyroll.AvailabilityKind);
                    Assert.Equal("https://www.crunchyroll.com/series/G4PH0WXVJ", crunchyroll.Url);
                },
                netflix =>
                {
                    Assert.Equal("netflix", netflix.ServiceId);
                    Assert.Equal("Netflix", netflix.DisplayName);
                    Assert.Equal("streaming", netflix.AvailabilityKind);
                    Assert.Equal("https://www.netflix.com/watch/81511410", netflix.Url);
                });
        Assert.Collection(
            details.Characters,
            anya =>
            {
                Assert.Equal("170732", anya.CharacterId);
                Assert.Equal("Anya Forger", anya.Name);
                Assert.Equal("main", anya.Role);
                Assert.Equal("https://example.test/anya-large.jpg", anya.ImageUrl);
                Assert.Equal("https://anilist.co/character/170732/Anya-Forger", anya.ProviderUrl);
                Assert.Equal(0, anya.Order);
            },
            becky =>
            {
                Assert.Equal("Becky Blackbell", becky.Name);
                Assert.Equal("supporting", becky.Role);
                Assert.Equal("https://example.test/becky.jpg", becky.ImageUrl);
                Assert.Equal(1, becky.Order);
            });
    }

    [Fact]
    public async Task DisconnectAsync_RemovesCredentialsAndLeavesEntriesDisconnected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(301, "disconnect@example.com");
        var account = new ConnectedServiceAccount
        {
            Id = 901,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "viewer-301",
            DisplayName = "Disconnect Tester",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Apothecary Diaries",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            CreatedAt = now,
            UpdatedAt = now
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "161645",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        };
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = link.Id,
            ConnectedServiceAccountId = account.Id,
            ProviderAccountId = account.ExternalAccountId,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaProviderLinks.Add(link);
        dbContext.MediaLibraryEntries.Add(entry);
        dbContext.MediaLibraryProviderBindings.Add(binding);
        await dbContext.SaveChangesAsync();

        var provider = CreateProvider(dbContext);
        await provider.DisconnectAsync(user.Id, CancellationToken.None);

        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();

        Assert.Empty(dbContext.ConnectedServiceAccounts);
        Assert.Null((await dbContext.MediaLibraryProviderBindings.SingleAsync()).ConnectedServiceAccountId);
        Assert.Equal(MediaMutationSources.ProviderDisconnect, persistedEntry.LastMutationSource);
    }

    [Fact]
    public async Task UpdateProgressAsync_AnimeEpisodeProgress_DoesNotSendNullProgressVolumesArgument()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(303, "anilist-progress@example.com");
        var now = DateTime.UtcNow;
        var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 903,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "303",
            DisplayName = "Progress Tester",
            EncryptedRefreshToken = CreateEncryptedToken(dataProtectionProvider, "access-token"),
            TokenExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        const string graphQlResponse = """
            {
                "data": {
                    "SaveMediaListEntry": {
                        "id": 42,
                        "status": "CURRENT",
                        "progress": 6,
                        "progressVolumes": null,
                        "updatedAt": 1780776000,
                        "media": {
                            "id": 154587
                        }
                    }
                }
            }
            """;

        var handler = new StubHttpMessageHandler(graphQlResponse);
        var provider = CreateProvider(dbContext, handler, dataProtectionProvider);

        await provider.UpdateProgressAsync(
            user.Id,
            new MediaProgressUpdateRequest
            {
                ProviderMediaId = "154587",
                ProgressEpisodes = 6
            },
            CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        using var document = JsonDocument.Parse(handler.LastRequestBody);
        var root = document.RootElement;
        var query = root.GetProperty("query").GetString();
        var variables = root.GetProperty("variables");

        Assert.Contains("progress: $progress", query);
        Assert.DoesNotContain("progressVolumes: $progressVolumes", query);
        Assert.DoesNotContain("status: $status", query);
        Assert.Equal(154587, variables.GetProperty("mediaId").GetInt32());
        Assert.Equal(6, variables.GetProperty("progress").GetInt32());
        Assert.False(variables.TryGetProperty("progressVolumes", out _));
        Assert.False(variables.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task UpdateStatusAsync_Repeating_MapsToAniListRepeating()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(304, "anilist-repeating@example.com");
        var now = DateTime.UtcNow;
        var dataProtectionProvider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 904,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "304",
            DisplayName = "Repeating Tester",
            EncryptedRefreshToken = CreateEncryptedToken(dataProtectionProvider, "access-token"),
            TokenExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        const string graphQlResponse = """
            {
                "data": {
                    "SaveMediaListEntry": {
                        "id": 43,
                        "status": "REPEATING",
                        "progress": 12,
                        "progressVolumes": null,
                        "updatedAt": 1780776000,
                        "media": { "id": 154587 }
                    }
                }
            }
            """;

        var handler = new StubHttpMessageHandler(graphQlResponse);
        var provider = CreateProvider(dbContext, handler, dataProtectionProvider);

        await provider.UpdateStatusAsync(
            user.Id,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = "154587",
                Status = MediaLibraryStatuses.Repeating
            },
            CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        using var document = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal("REPEATING", document.RootElement.GetProperty("variables").GetProperty("status").GetString());
    }

    [Fact]
    public async Task SyncLibraryStateAsync_UpsertsCompleteStateWithOneMediaIdMutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(308, "anilist-whole-state@example.com");
        var now = DateTime.UtcNow;
        var dataProtectionProvider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 908,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "308",
            DisplayName = "Whole State Tester",
            EncryptedRefreshToken = CreateEncryptedToken(dataProtectionProvider, "access-token"),
            TokenExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        const string graphQlResponse = """
            { "data": { "SaveMediaListEntry": {
              "id": 45, "status": "COMPLETED", "score": 0, "progress": 168,
              "progressVolumes": 23, "updatedAt": 1780776000,
              "media": { "id": 87610 }
            } } }
            """;
        var handler = new StubHttpMessageHandler(graphQlResponse);
        var provider = CreateProvider(dbContext, handler, dataProtectionProvider);

        var result = await provider.SyncLibraryStateAsync(
            user.Id,
            new MediaLibraryStateSyncRequest
            {
                ProviderMediaId = "87610",
                Status = MediaLibraryStatuses.Completed,
                Score = null,
                ProgressChapters = 168,
                ProgressVolumes = 23
            },
            CancellationToken.None);

        Assert.Equal("87610", result.ProviderMediaId);
        Assert.NotNull(handler.LastRequestBody);
        using var document = JsonDocument.Parse(handler.LastRequestBody);
        var root = document.RootElement;
        var query = root.GetProperty("query").GetString();
        var variables = root.GetProperty("variables");
        Assert.Contains("SaveMediaListEntry", query);
        Assert.Contains("progress: $progress", query);
        Assert.Contains("progressVolumes: $progressVolumes", query);
        Assert.Contains("status: $status", query);
        Assert.Contains("scoreRaw: $scoreRaw", query);
        Assert.Equal(87610, variables.GetProperty("mediaId").GetInt32());
        Assert.Equal(168, variables.GetProperty("progress").GetInt32());
        Assert.Equal(23, variables.GetProperty("progressVolumes").GetInt32());
        Assert.Equal("COMPLETED", variables.GetProperty("status").GetString());
        Assert.Equal(0, variables.GetProperty("scoreRaw").GetInt32());
        Assert.False(variables.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task UpdateScoreAsync_WritesRoundedScoreRawAndClearsWithZero()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(307, "anilist-score@example.com");
        var now = DateTime.UtcNow;
        var dataProtectionProvider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 907,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "307",
            DisplayName = "Score Tester",
            EncryptedRefreshToken = CreateEncryptedToken(dataProtectionProvider, "access-token"),
            TokenExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        const string graphQlResponse = """
            { "data": { "SaveMediaListEntry": {
              "id": 44, "status": "CURRENT", "score": 82.0, "updatedAt": 1780776000,
              "media": { "id": 154587 }
            } } }
            """;
        var handler = new StubHttpMessageHandler(graphQlResponse);
        var provider = CreateProvider(dbContext, handler, dataProtectionProvider);

        await provider.UpdateScoreAsync(
            user.Id,
            new MediaScoreUpdateRequest { ProviderMediaId = "154587", Score = 82.5m },
            CancellationToken.None);

        using (var firstRequest = JsonDocument.Parse(handler.LastRequestBody!))
        {
            var variables = firstRequest.RootElement.GetProperty("variables");
            Assert.Contains("scoreRaw: $scoreRaw", firstRequest.RootElement.GetProperty("query").GetString());
            Assert.Equal(83, variables.GetProperty("scoreRaw").GetInt32());
        }

        await provider.UpdateScoreAsync(
            user.Id,
            new MediaScoreUpdateRequest { ProviderMediaId = "154587", Score = null },
            CancellationToken.None);

        using var clearRequest = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(0, clearRequest.RootElement.GetProperty("variables").GetProperty("scoreRaw").GetInt32());
    }

    [Fact]
    public async Task ImportLibraryAsync_AggregatesDuplicateGroupsAndKeepsOnlyCustomLists()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(305, "anilist-custom-lists@example.com");
        var now = DateTime.UtcNow;
        var dataProtectionProvider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 905,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "305",
            DisplayName = "Custom List Tester",
            EncryptedRefreshToken = CreateEncryptedToken(dataProtectionProvider, "access-token"),
            TokenExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        const string graphQlResponse = """
            {
              "data": {
                "MediaListCollection": {
                  "lists": [
                    {
                      "name": "Watching",
                      "isCustomList": false,
                      "entries": [{
                        "id": 42,
                        "status": "REPEATING",
                        "score": 82.3,
                        "progress": 6,
                        "progressVolumes": null,
                        "updatedAt": 1780776000,
                        "media": {
                          "id": 154587,
                          "type": "ANIME",
                          "description": "An elf revisits old memories.",
                          "episodes": 28,
                          "title": { "romaji": "Sousou no Frieren", "english": "Frieren", "native": "葬送のフリーレン" }
                        }
                      }]
                    },
                    {
                      "name": "Favorites",
                      "isCustomList": true,
                      "entries": [{
                        "id": 42,
                        "status": "REPEATING",
                        "score": 82.3,
                        "progress": 6,
                        "progressVolumes": null,
                        "updatedAt": 1780776000,
                        "media": {
                          "id": 154587,
                          "type": "ANIME",
                          "description": "An elf revisits old memories.",
                          "episodes": 28,
                          "title": { "romaji": "Sousou no Frieren", "english": "Frieren", "native": "葬送のフリーレン" }
                        }
                      }]
                    }
                  ]
                }
              }
            }
            """;

        var handler = new StubHttpMessageHandler(graphQlResponse);
        var provider = CreateProvider(
            dbContext,
            handler,
            dataProtectionProvider);

        var import = await provider.ImportLibraryAsync(user.Id, CancellationToken.None);

        var item = Assert.Single(import.Items);
        Assert.Equal(MediaLibraryStatuses.Repeating, item.Status);
        Assert.Equal(82.3m, item.Score);
        Assert.Equal(["Favorites"], item.ProviderListNames);
        Assert.Contains("score(format: POINT_100)", handler.LastRequestBody);
    }

    private static AniListMediaProvider CreateProvider(
        ApplicationDbContext dbContext,
        HttpMessageHandler? handler = null,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        var apiClient = new AniListApiClient(
            new HttpClient(handler ?? new StubHttpMessageHandler()),
            Options.Create(new AniListOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret"
            }),
            new AniListRequestGate(TimeProvider.System, TimeSpan.Zero),
            NullLogger<AniListApiClient>.Instance);
        dataProtectionProvider ??= DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var tokenEncryption = new TokenEncryptionService(dataProtectionProvider);

        return new AniListMediaProvider(dbContext, apiClient, tokenEncryption, NullLogger<AniListMediaProvider>.Instance);
    }

    private static string CreateEncryptedToken(IDataProtectionProvider dataProtectionProvider, string token)
    {
        var tokenEncryption = new TokenEncryptionService(dataProtectionProvider);
        return tokenEncryption.Encrypt(token);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubHttpMessageHandler(string responseBody = "{}")
        {
            _responseBody = responseBody;
        }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class QueueHttpMessageHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException("No queued AniList response remains.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
