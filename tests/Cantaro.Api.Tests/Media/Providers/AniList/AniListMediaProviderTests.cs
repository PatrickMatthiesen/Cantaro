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

        var provider = CreateProvider(dbContext, new StubHttpMessageHandler(graphQlResponse), dataProtectionProvider);

        var details = await provider.GetTitleDetailsAsync(user.Id, "140960", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("https://example.test/poster-extra-large.jpg", details.PosterUrl);
        using (var rawMetadata = JsonDocument.Parse(details.RawMetadata!))
        {
            Assert.Equal(
                "https://example.test/poster-extra-large.jpg",
                rawMetadata.RootElement.GetProperty("coverImage").GetProperty("extraLarge").GetString());
        }
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

        var provider = CreateProvider(
            dbContext,
            new StubHttpMessageHandler(graphQlResponse),
            dataProtectionProvider);

        var import = await provider.ImportLibraryAsync(user.Id, CancellationToken.None);

        var item = Assert.Single(import.Items);
        Assert.Equal(MediaLibraryStatuses.Repeating, item.Status);
        Assert.Equal(["Favorites"], item.ProviderListNames);
        using var rawMetadata = JsonDocument.Parse(item.RawMetadata!);
        Assert.Equal("REPEATING", rawMetadata.RootElement.GetProperty("status").GetString());
        Assert.True(rawMetadata.RootElement.TryGetProperty("media", out _));
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
}
