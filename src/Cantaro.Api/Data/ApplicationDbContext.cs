using Cantaro.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Data;

public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>
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
    }
}
