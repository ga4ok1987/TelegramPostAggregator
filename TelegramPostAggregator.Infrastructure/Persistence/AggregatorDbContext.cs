using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Infrastructure.Persistence;

public sealed class AggregatorDbContext(DbContextOptions<AggregatorDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<TrackedChannel> TrackedChannels => Set<TrackedChannel>();
    public DbSet<UserChannelSubscription> UserChannelSubscriptions => Set<UserChannelSubscription>();
    public DbSet<ManagedChannel> ManagedChannels => Set<ManagedChannel>();
    public DbSet<CollectorAccount> CollectorAccounts => Set<CollectorAccount>();
    public DbSet<ChannelCollectorAssignment> ChannelCollectorAssignments => Set<ChannelCollectorAssignment>();
    public DbSet<TelegramPost> TelegramPosts => Set<TelegramPost>();
    public DbSet<FactCheckRequest> FactCheckRequests => Set<FactCheckRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Domain.Common.BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Domain.Common.BaseEntity.CreatedAtUtc)).HasDefaultValueSql("CURRENT_TIMESTAMP");
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Domain.Common.BaseEntity.UpdatedAtUtc)).HasDefaultValueSql("CURRENT_TIMESTAMP");
            }
        }

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasIndex(x => x.TelegramUserId).IsUnique();
            entity.Property(x => x.TelegramUsername).HasMaxLength(128);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.PreferredLanguageCode).HasMaxLength(16);
        });

        modelBuilder.Entity<TrackedChannel>(entity =>
        {
            entity.ToTable("tracked_channels");
            entity.HasIndex(x => x.NormalizedChannelKey).IsUnique();
            entity.HasIndex(x => x.TelegramChannelId);
            entity.Property(x => x.ChannelName).HasMaxLength(256);
            entity.Property(x => x.UsernameOrInviteLink).HasMaxLength(512);
            entity.Property(x => x.NormalizedChannelKey).HasMaxLength(256);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.LastCollectorError).HasMaxLength(2048);
        });

        modelBuilder.Entity<UserChannelSubscription>(entity =>
        {
            entity.ToTable("user_channel_subscriptions");
            entity.HasIndex(x => new { x.UserId, x.ChannelId }).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.ChannelSubscriptions).HasForeignKey(x => x.UserId);
            entity.HasOne(x => x.Channel).WithMany(x => x.UserSubscriptions).HasForeignKey(x => x.ChannelId);
            entity.HasIndex(x => new { x.IsActive, x.LastDeliveredTelegramMessageId });
        });

        modelBuilder.Entity<ManagedChannel>(entity =>
        {
            entity.ToTable("managed_channels");
            entity.HasIndex(x => new { x.UserId, x.TelegramChatId }).IsUnique();
            entity.Property(x => x.ChannelName).HasMaxLength(256);
            entity.Property(x => x.Username).HasMaxLength(256);
            entity.Property(x => x.LastWriteError).HasMaxLength(2048);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<CollectorAccount>(entity =>
        {
            entity.ToTable("collector_accounts");
            entity.HasIndex(x => x.ExternalAccountKey).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.ExternalAccountKey).HasMaxLength(128);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
            entity.Property(x => x.SerializedSession).HasColumnType("text");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.LastError).HasMaxLength(2048);
        });

        modelBuilder.Entity<ChannelCollectorAssignment>(entity =>
        {
            entity.ToTable("channel_collector_assignments");
            entity.HasIndex(x => new { x.ChannelId, x.CollectorAccountId }).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.LastError).HasMaxLength(2048);
            entity.HasOne(x => x.Channel).WithMany(x => x.CollectorAssignments).HasForeignKey(x => x.ChannelId);
            entity.HasOne(x => x.CollectorAccount).WithMany(x => x.ChannelAssignments).HasForeignKey(x => x.CollectorAccountId);
        });

        modelBuilder.Entity<TelegramPost>(entity =>
        {
            entity.ToTable("telegram_posts");
            entity.HasIndex(x => new { x.ChannelId, x.TelegramMessageId }).IsUnique();
            entity.Property(x => x.AuthorSignature).HasMaxLength(256);
            entity.Property(x => x.RawText).HasColumnType("text");
            entity.Property(x => x.NormalizedText).HasColumnType("text");
            entity.Property(x => x.MediaGroupId).HasMaxLength(128);
            entity.Property(x => x.OriginalPostUrl).HasMaxLength(1024);
            entity.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(64).HasDefaultValue(PostSourceKind.ChannelPost);
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb");
            entity.HasOne(x => x.Channel).WithMany(x => x.Posts).HasForeignKey(x => x.ChannelId);
            entity.HasOne(x => x.CollectorAccount).WithMany().HasForeignKey(x => x.CollectorAccountId);
        });

        modelBuilder.Entity<FactCheckRequest>(entity =>
        {
            entity.ToTable("fact_check_requests");
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Prompt).HasColumnType("text");
            entity.Property(x => x.ResultSummary).HasColumnType("text");
            entity.Property(x => x.SupportingEvidenceJson).HasColumnType("jsonb");
            entity.Property(x => x.ProviderName).HasMaxLength(128);
            entity.Property(x => x.ProviderRequestId).HasMaxLength(256);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
            entity.Property(x => x.CredibilityScore).HasPrecision(5, 2);
            entity.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId);
            entity.HasOne(x => x.RequestedByUser).WithMany(x => x.FactCheckRequests).HasForeignKey(x => x.RequestedByUserId);
        });
    }
}
