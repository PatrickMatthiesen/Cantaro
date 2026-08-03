using Cantaro.Api.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Cantaro.Api.Data;

public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ConnectedServiceAccount> ConnectedServiceAccounts => Set<ConnectedServiceAccount>();
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<SongTrack> SongTracks => Set<SongTrack>();
    public DbSet<SongCredit> SongCredits => Set<SongCredit>();
    public DbSet<SongGroupingSuggestion> SongGroupingSuggestions => Set<SongGroupingSuggestion>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackRelation> TrackRelations => Set<TrackRelation>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<TrackArtistCredit> TrackArtistCredits => Set<TrackArtistCredit>();
    public DbSet<TrackSourceId> TrackSourceIds => Set<TrackSourceId>();
    public DbSet<TrackObservation> TrackObservations => Set<TrackObservation>();
    public DbSet<TrackResolutionCandidate> TrackResolutionCandidates => Set<TrackResolutionCandidate>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistEntry> PlaylistEntries => Set<PlaylistEntry>();
    public DbSet<ServicePlaylistMapping> ServicePlaylistMappings => Set<ServicePlaylistMapping>();
    public DbSet<MusicSyncJob> MusicSyncJobs => Set<MusicSyncJob>();
    public DbSet<MediaTitle> MediaTitles => Set<MediaTitle>();
    public DbSet<MediaProviderLink> MediaProviderLinks => Set<MediaProviderLink>();
    public DbSet<MediaEpisode> MediaEpisodes => Set<MediaEpisode>();
    public DbSet<MediaEpisodeProviderIdentity> MediaEpisodeProviderIdentities => Set<MediaEpisodeProviderIdentity>();
    public DbSet<MediaLibraryEntry> MediaLibraryEntries => Set<MediaLibraryEntry>();
    public DbSet<MediaProviderOperation> MediaProviderOperations => Set<MediaProviderOperation>();
    public DbSet<MediaObservation> MediaObservations => Set<MediaObservation>();
    public DbSet<MediaObservationCandidate> MediaObservationCandidates => Set<MediaObservationCandidate>();
    public DbSet<MediaObservationEpisodeOffset> MediaObservationEpisodeOffsets => Set<MediaObservationEpisodeOffset>();
    public DbSet<ExtensionAuthorizationCode> ExtensionAuthorizationCodes => Set<ExtensionAuthorizationCode>();
    public DbSet<ExtensionRefreshToken> ExtensionRefreshTokens => Set<ExtensionRefreshToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    public override int SaveChanges()
    {
        PrepareChanges();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.DisplayName).HasMaxLength(100);
            entity.Property(e => e.Theme).HasMaxLength(16);
            entity.Property(e => e.AvatarObjectKey).HasMaxLength(512);
            entity.Property(e => e.AvatarETag).HasMaxLength(128);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithOne(u => u.Settings)
                .HasForeignKey<UserSettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConnectedServiceAccount>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.Service })
                .IsUnique();

            entity.HasIndex(e => e.ExternalAccountId);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.ConnectionState).HasMaxLength(32).HasDefaultValue("connected");
            entity.Property(e => e.ReconnectReason).HasMaxLength(128);
            entity.HasIndex(e => e.TokenRefreshLeaseExpiresAt);

            entity.HasOne(e => e.User)
                .WithMany(u => u.ConnectedServiceAccounts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExtensionRefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            entity.HasIndex(e => new { e.UserId, e.ClientId });
            entity.HasIndex(e => e.ExpiresAt);

            entity.Property(e => e.ClientId)
                .HasMaxLength(200);

            entity.Property(e => e.TokenHash)
                .HasMaxLength(256);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExtensionAuthorizationCode>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.CodeHash)
                .IsUnique();

            entity.HasIndex(e => e.ExpiresAt);

            entity.Property(e => e.ClientId)
                .HasMaxLength(200);

            entity.Property(e => e.RedirectUri)
                .HasMaxLength(2048);

            entity.Property(e => e.CodeChallenge)
                .HasMaxLength(256);

            entity.Property(e => e.CodeHash)
                .HasMaxLength(256);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Song>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<SongTrack>(entity =>
        {
            entity.HasKey(e => new { e.SongId, e.TrackId });
            entity.HasIndex(e => new { e.TrackId, e.SongId });

            entity.HasOne(e => e.Song)
                .WithMany(song => song.TrackMemberships)
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Track)
                .WithMany(track => track.SongMemberships)
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SongGroupingSuggestion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(32);
            entity.Property(e => e.Status).HasMaxLength(16);
            entity.Property(e => e.Confidence).HasPrecision(5, 4);
            entity.Property(e => e.EvidenceJson).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
            entity.HasIndex(e => new { e.CandidateTrackId, e.TargetSongId })
                .IsUnique();

            entity.HasOne(e => e.CandidateTrack)
                .WithMany()
                .HasForeignKey(e => e.CandidateTrackId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AnchorTrack)
                .WithMany()
                .HasForeignKey(e => e.AnchorTrackId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceSong)
                .WithMany()
                .HasForeignKey(e => e.SourceSongId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TargetSong)
                .WithMany()
                .HasForeignKey(e => e.TargetSongId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedByUser)
                .WithMany()
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Track>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.VersionFlags).HasConversion<long>();
            entity.Property(e => e.VersionEvidence).HasColumnType("jsonb");
            entity.Property(e => e.SearchTitle).HasMaxLength(512);
            entity.Property(e => e.SearchArtist).HasMaxLength(1024);

            entity.HasIndex(e => e.MbidRecording);
            entity.HasIndex(e => e.Isrc);
        });

        modelBuilder.Entity<Artist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MusicBrainzArtistId)
                .HasMaxLength(36)
                .HasConversion(
                    value => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant(),
                    value => value);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Artist names are intentionally not unique. Only a stable external
            // identity is safe to use when reconciling artist records.
            entity.HasIndex(e => e.MusicBrainzArtistId)
                .IsUnique()
                .HasFilter("\"MusicBrainzArtistId\" IS NOT NULL");
        });

        modelBuilder.Entity<TrackArtistCredit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasConversion<short>();

            entity.HasIndex(e => new { e.TrackId, e.Position }).IsUnique();
            entity.HasIndex(e => new { e.TrackId, e.ArtistId, e.Role }).IsUnique();

            entity.HasOne(e => e.Track)
                .WithMany(t => t.ArtistCredits)
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Artist)
                .WithMany(a => a.TrackCredits)
                .HasForeignKey(e => e.ArtistId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SongCredit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasConversion<short>();

            entity.HasIndex(e => new { e.SongId, e.Position }).IsUnique();
            entity.HasIndex(e => new { e.SongId, e.ArtistId, e.Role }).IsUnique();

            entity.HasOne(e => e.Song)
                .WithMany(song => song.Credits)
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Artist)
                .WithMany(artist => artist.SongCredits)
                .HasForeignKey(e => e.ArtistId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TrackRelation>(entity =>
        {
            entity.HasKey(e => new { e.FromTrackId, e.ToTrackId, e.RelationType });
            entity.Property(e => e.RelationType).HasConversion<short>();
            entity.HasIndex(e => new { e.ToTrackId, e.RelationType, e.FromTrackId });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_TrackRelations_NoSelfRelation",
                "\"FromTrackId\" <> \"ToTrackId\""));

            entity.HasOne(e => e.FromTrack)
                .WithMany(track => track.OutgoingRelations)
                .HasForeignKey(e => e.FromTrackId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ToTrack)
                .WithMany(track => track.IncomingRelations)
                .HasForeignKey(e => e.ToTrackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackSourceId>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PresentationKind).HasConversion<short>();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_TrackSourceIds_PresentationConfidence",
                "\"PresentationConfidence\" IS NULL OR (\"PresentationConfidence\" >= 0 AND \"PresentationConfidence\" <= 100)"));

            // Unique index on (SourceType, ExternalId)
            entity.HasIndex(e => new { e.SourceType, e.ExternalId })
                .IsUnique();

            entity.HasOne(e => e.Track)
                .WithMany(t => t.SourceIds)
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackObservation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.SourceType, e.ExternalId })
                .IsUnique();

            entity.HasIndex(e => e.MatchStatus);
            entity.HasIndex(e => new
            {
                e.NormalizedTitle,
                e.DurationSeconds,
                e.TrackId,
                e.SourceType
            }).HasDatabaseName("IX_TrackObservations_IdentityLookup");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Track)
                .WithMany()
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Candidates)
                .WithOne(c => c.TrackObservation)
                .HasForeignKey(c => c.TrackObservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackResolutionCandidate>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.TrackObservationId, e.Score });
        });

        modelBuilder.Entity<Playlist>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.ImportedFromService).HasMaxLength(64);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaylistEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Unique index on (PlaylistId, Position)
            entity.HasIndex(e => new { e.PlaylistId, e.Position })
                .IsUnique();
            entity.HasIndex(e => new { e.PlaylistId, e.TrackId })
                .IsUnique()
                .HasFilter("\"TrackId\" IS NOT NULL");

            entity.HasOne(e => e.Playlist)
                .WithMany(p => p.Entries)
                .HasForeignKey(e => e.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Track)
                .WithMany(t => t.PlaylistEntries)
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TrackObservation)
                .WithMany(o => o.PlaylistEntries)
                .HasForeignKey(e => e.TrackObservationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ServicePlaylistMapping>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Unique index on (PlaylistId, Service)
            entity.HasIndex(e => new { e.PlaylistId, e.Service })
                .IsUnique();

            entity.HasOne(e => e.Playlist)
                .WithMany(p => p.ServiceMappings)
                .HasForeignKey(e => e.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ConnectedServiceAccount)
                .WithMany(a => a.ServicePlaylistMappings)
                .HasForeignKey(e => e.ConnectedServiceAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MusicSyncJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Status, e.CreatedAt });
            entity.HasIndex(e => new { e.UserId, e.Service })
                .IsUnique()
                .HasFilter("\"Status\" IN ('queued', 'running')");
            entity.Property(e => e.Service).HasMaxLength(64);
            entity.Property(e => e.Status).HasMaxLength(32);
            entity.Property(e => e.CurrentSongName).HasMaxLength(512);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaTitle>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.MediaKind, e.CanonicalTitle });
        });

        modelBuilder.Entity<MediaProviderLink>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.MediaTitleId, e.Provider })
                .IsUnique();

            entity.HasIndex(e => new { e.Provider, e.ExternalId })
                .IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.MediaTitle)
                .WithMany(t => t.ProviderLinks)
                .HasForeignKey(e => e.MediaTitleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LinkedByUser)
                .WithMany()
                .HasForeignKey(e => e.LinkedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaEpisode>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.MediaTitleId, e.EpisodeNumber })
                .IsUnique();

            entity.Property(e => e.Title).HasMaxLength(512);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_MediaEpisodes_EpisodeNumber",
                "\"EpisodeNumber\" > 0"));

            entity.HasOne(e => e.MediaTitle)
                .WithMany(t => t.Episodes)
                .HasForeignKey(e => e.MediaTitleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaEpisodeProviderIdentity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.Provider, e.ProviderEpisodeId })
                .IsUnique();

            entity.HasIndex(e => new { e.MediaEpisodeId, e.Provider, e.HasConflict });

            entity.Property(e => e.Provider).HasMaxLength(64);
            entity.Property(e => e.ProviderSeriesId).HasMaxLength(256);
            entity.Property(e => e.ProviderSeasonId).HasMaxLength(256);
            entity.Property(e => e.ProviderEpisodeId).HasMaxLength(256);
            entity.Property(e => e.ProviderUrlPath).HasMaxLength(1024);
            entity.Property(e => e.SeenCount).HasDefaultValue(1);

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_MediaEpisodeProviderIdentities_SeenCount",
                "\"SeenCount\" > 0"));

            entity.HasOne(e => e.MediaEpisode)
                .WithMany(e => e.ProviderIdentities)
                .HasForeignKey(e => e.MediaEpisodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaLibraryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.Provider, e.ProviderAccountId, e.ProviderMediaId })
                .IsUnique();

            entity.HasIndex(e => new { e.UserId, e.NormalizedStatus });

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany(u => u.MediaLibraryEntries)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MediaTitle)
                .WithMany(t => t.LibraryEntries)
                .HasForeignKey(e => e.MediaTitleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ConnectedServiceAccount)
                .WithMany(a => a.MediaLibraryEntries)
                .HasForeignKey(e => e.ConnectedServiceAccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaProviderOperation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            entity.HasIndex(e => new { e.UserId, e.Provider, e.CreatedAt });
            entity.HasIndex(e => e.MediaLibraryEntryId);
            entity.HasIndex(e => e.ConnectedServiceAccountId);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany(u => u.MediaProviderOperations)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MediaLibraryEntry)
                .WithMany(entry => entry.ProviderOperations)
                .HasForeignKey(e => e.MediaLibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ConnectedServiceAccount)
                .WithMany(account => account.MediaProviderOperations)
                .HasForeignKey(e => e.ConnectedServiceAccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaObservation>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Deduplicate stable-ID observations per user per site.
            entity.HasIndex(e => new { e.UserId, e.SiteIdentifier, e.SiteMediaId })
                .IsUnique()
                .HasFilter("\"SiteMediaId\" IS NOT NULL");

            entity.HasIndex(e => new { e.UserId, e.MatchStatus });
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MediaTitle)
                .WithMany()
                .HasForeignKey(e => e.MediaTitleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Candidates)
                .WithOne(c => c.MediaObservation)
                .HasForeignKey(c => c.MediaObservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaObservationCandidate>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.MediaObservationId, e.Score });

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.MediaTitle)
                .WithMany()
                .HasForeignKey(e => e.MediaTitleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MediaObservationEpisodeOffset>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.SiteIdentifier, e.MediaTitleId })
                .IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MediaTitle)
                .WithMany()
                .HasForeignKey(e => e.MediaTitleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void UpdateUserTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private void PrepareChanges()
    {
        UpdateUserTimestamps();
        UpdateTrackSearchProjections();
    }

    private void UpdateTrackSearchProjections()
    {
        foreach (var entry in ChangeTracker.Entries<Track>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || (entry.State == EntityState.Modified
                    && !entry.Property(track => track.CanonicalMetadata).IsModified))
            {
                continue;
            }

            var metadata = ParseTrackSearchMetadata(entry.Entity.CanonicalMetadata);
            entry.Entity.SearchTitle = TrimToLength(metadata.Title, 512);
            entry.Entity.SearchArtist = TrimToLength(metadata.Artist, 1024);
        }
    }

    private static (string? Title, string? Artist) ParseTrackSearchMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            return (
                ReadString(document.RootElement, "Title", "title"),
                ReadString(document.RootElement, "Artist", "artist"));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ReadString(
        JsonElement element,
        string propertyName,
        string fallbackPropertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            && !element.TryGetProperty(fallbackPropertyName, out property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
