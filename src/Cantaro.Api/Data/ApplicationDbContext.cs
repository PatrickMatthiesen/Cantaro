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
    public DbSet<Track> Tracks => Set<Track>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
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

        modelBuilder.Entity<Track>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.MbidRecording);
            entity.HasIndex(e => e.Isrc);
        });

        modelBuilder.Entity<TrackSourceId>(entity =>
        {
            entity.HasKey(e => e.Id);

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
}
