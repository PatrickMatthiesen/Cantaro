using Cantaro.Api.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Data;

public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ConnectedServiceAccount> ConnectedServiceAccounts => Set<ConnectedServiceAccount>();
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<TrackArtistCredit> TrackArtistCredits => Set<TrackArtistCredit>();
    public DbSet<TrackVersionTrait> TrackVersionTraits => Set<TrackVersionTrait>();
    public DbSet<TrackSourceId> TrackSourceIds => Set<TrackSourceId>();
    public DbSet<TrackObservation> TrackObservations => Set<TrackObservation>();
    public DbSet<TrackResolutionCandidate> TrackResolutionCandidates => Set<TrackResolutionCandidate>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistEntry> PlaylistEntries => Set<PlaylistEntry>();
    public DbSet<ServicePlaylistMapping> ServicePlaylistMappings => Set<ServicePlaylistMapping>();
    public DbSet<MusicSyncJob> MusicSyncJobs => Set<MusicSyncJob>();
    public DbSet<MediaTitle> MediaTitles => Set<MediaTitle>();
    public DbSet<MediaProviderLink> MediaProviderLinks => Set<MediaProviderLink>();
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
        UpdateUserTimestamps();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateUserTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateUserTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        UpdateUserTimestamps();
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

        modelBuilder.Entity<Track>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.MbidRecording);
            entity.HasIndex(e => e.Isrc);

            entity.HasIndex(e => e.SongId);
            entity.HasOne(e => e.Song)
                .WithMany(song => song.Tracks)
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(32);

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

        modelBuilder.Entity<TrackVersionTrait>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TraitKey).HasMaxLength(64);
            entity.Property(e => e.Confidence).HasPrecision(5, 4);
            entity.Property(e => e.EvidenceSource).HasMaxLength(64);
            entity.Property(e => e.EvidenceIdentity).HasMaxLength(256);
            entity.Property(e => e.EvidenceMethod).HasMaxLength(96);
            entity.Property(e => e.MethodVersion).HasMaxLength(64);
            entity.Property(e => e.AssertedByType).HasMaxLength(24);
            entity.Property(e => e.AssertedById).HasMaxLength(128);
            entity.Property(e => e.RevokedByType).HasMaxLength(24);
            entity.Property(e => e.RevokedById).HasMaxLength(128);
            entity.Property(e => e.RevocationReason).HasMaxLength(256);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_TrackVersionTraits_Confidence",
                    "CAST(\"Confidence\" AS REAL) >= 0 AND CAST(\"Confidence\" AS REAL) <= 1");
                table.HasCheckConstraint(
                    "CK_TrackVersionTraits_Revocation",
                    "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                table.HasCheckConstraint(
                    "CK_TrackVersionTraits_RevocationAudit",
                    "(\"RevokedAt\" IS NULL AND \"RevokedByType\" IS NULL AND \"RevokedById\" IS NULL AND \"RevocationReason\" IS NULL) OR " +
                    "(\"RevokedAt\" IS NOT NULL AND \"RevokedByType\" IS NOT NULL AND \"RevokedById\" IS NOT NULL AND \"RevocationReason\" IS NOT NULL)");
            });

            entity.HasIndex(e => new { e.TrackId, e.RevokedAt, e.TraitKey })
                .HasDatabaseName("IX_TrackVersionTraits_Track_Active_Trait");
            entity.HasIndex(e => e.SupersedesTraitId);
            entity.HasIndex(e => new
                {
                    e.TrackId,
                    e.TraitKey,
                    e.EvidenceSource,
                    e.EvidenceIdentity,
                    e.EvidenceMethod
                })
                .IsUnique()
                .HasFilter("\"RevokedAt\" IS NULL")
                .HasDatabaseName("UX_TrackVersionTraits_ActiveEvidence");

            entity.HasOne(e => e.Track)
                .WithMany(track => track.VersionTraits)
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SupersedesTrait)
                .WithMany()
                .HasForeignKey(e => e.SupersedesTraitId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TrackSourceId>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.PresentationKind).HasMaxLength(32);
            entity.Property(e => e.PresentationKindConfidence).HasPrecision(5, 4);
            entity.Property(e => e.PresentationKindEvidenceSource).HasMaxLength(64);
            entity.Property(e => e.PresentationKindEvidenceIdentity).HasMaxLength(256);
            entity.Property(e => e.PresentationKindEvidenceMethod).HasMaxLength(96);
            entity.Property(e => e.PresentationKindMethodVersion).HasMaxLength(64);
            entity.Property(e => e.UploaderAuthority).HasMaxLength(16);
            entity.Property(e => e.UploaderAuthorityConfidence).HasPrecision(5, 4);
            entity.Property(e => e.UploaderAuthorityEvidenceSource).HasMaxLength(64);
            entity.Property(e => e.UploaderAuthorityEvidenceIdentity).HasMaxLength(256);
            entity.Property(e => e.UploaderAuthorityEvidenceMethod).HasMaxLength(96);
            entity.Property(e => e.UploaderAuthorityMethodVersion).HasMaxLength(64);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_TrackSourceIds_PresentationKindConfidence",
                    "\"PresentationKindConfidence\" IS NULL OR " +
                    "(CAST(\"PresentationKindConfidence\" AS REAL) >= 0 AND " +
                    "CAST(\"PresentationKindConfidence\" AS REAL) <= 1)");
                table.HasCheckConstraint(
                    "CK_TrackSourceIds_PresentationKindBundle",
                    "(\"PresentationKind\" IS NULL AND \"PresentationKindConfidence\" IS NULL AND " +
                    "\"PresentationKindEvidenceSource\" IS NULL AND \"PresentationKindEvidenceIdentity\" IS NULL AND " +
                    "\"PresentationKindEvidenceMethod\" IS NULL AND \"PresentationKindMethodVersion\" IS NULL AND " +
                    "\"PresentationKindClassifiedAt\" IS NULL AND \"PresentationKindRevision\" IS NULL) OR " +
                    "(\"PresentationKind\" IS NOT NULL AND \"PresentationKindConfidence\" IS NOT NULL AND " +
                    "\"PresentationKindEvidenceSource\" IS NOT NULL AND \"PresentationKindEvidenceIdentity\" IS NOT NULL AND " +
                    "\"PresentationKindEvidenceMethod\" IS NOT NULL AND \"PresentationKindClassifiedAt\" IS NOT NULL AND " +
                    "\"PresentationKindRevision\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_TrackSourceIds_UploaderAuthorityConfidence",
                    "\"UploaderAuthorityConfidence\" IS NULL OR " +
                    "(CAST(\"UploaderAuthorityConfidence\" AS REAL) >= 0 AND " +
                    "CAST(\"UploaderAuthorityConfidence\" AS REAL) <= 1)");
                table.HasCheckConstraint(
                    "CK_TrackSourceIds_UploaderAuthorityBundle",
                    "(\"UploaderAuthority\" IS NULL AND \"UploaderAuthorityConfidence\" IS NULL AND " +
                    "\"UploaderAuthorityEvidenceSource\" IS NULL AND \"UploaderAuthorityEvidenceIdentity\" IS NULL AND " +
                    "\"UploaderAuthorityEvidenceMethod\" IS NULL AND \"UploaderAuthorityMethodVersion\" IS NULL AND " +
                    "\"UploaderAuthorityClassifiedAt\" IS NULL AND \"UploaderAuthorityRevision\" IS NULL) OR " +
                    "(\"UploaderAuthority\" IS NOT NULL AND \"UploaderAuthorityConfidence\" IS NOT NULL AND " +
                    "\"UploaderAuthorityEvidenceSource\" IS NOT NULL AND \"UploaderAuthorityEvidenceIdentity\" IS NOT NULL AND " +
                    "\"UploaderAuthorityEvidenceMethod\" IS NOT NULL AND \"UploaderAuthorityClassifiedAt\" IS NOT NULL AND " +
                    "\"UploaderAuthorityRevision\" IS NOT NULL)");
            });

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
}
