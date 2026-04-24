using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUsername = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PreferredLanguageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsBlockedBot = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "collector_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalAccountKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SerializedSession = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collector_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tracked_channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramChannelId = table.Column<string>(type: "text", nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UsernameOrInviteLink = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NormalizedChannelKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastPostCollectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSubscriptionAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCollectorError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracked_channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channel_collector_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_collector_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_channel_collector_assignments_collector_accounts_CollectorA~",
                        column: x => x.CollectorAccountId,
                        principalTable: "collector_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_channel_collector_assignments_tracked_channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "tracked_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_channel_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_channel_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_channel_subscriptions_app_users_UserId",
                        column: x => x.UserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_channel_subscriptions_tracked_channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "tracked_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fact_check_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    ResultSummary = table.Column<string>(type: "text", nullable: true),
                    SupportingEvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderRequestId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CredibilityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fact_check_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fact_check_requests_app_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_duplicate_clusters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalPostId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClusterKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SummaryNormalizedText = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_duplicate_clusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "telegram_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DuplicateClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AuthorSignature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RawText = table.Column<string>(type: "text", nullable: false),
                    NormalizedText = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MediaGroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    HasMedia = table.Column<bool>(type: "boolean", nullable: false),
                    IsForwarded = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalPostUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SourceKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "ChannelPost"),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_posts_collector_accounts_CollectorAccountId",
                        column: x => x.CollectorAccountId,
                        principalTable: "collector_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_telegram_posts_post_duplicate_clusters_DuplicateClusterId",
                        column: x => x.DuplicateClusterId,
                        principalTable: "post_duplicate_clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_telegram_posts_tracked_channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "tracked_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_users_TelegramUserId",
                table: "app_users",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_collector_assignments_ChannelId_CollectorAccountId",
                table: "channel_collector_assignments",
                columns: new[] { "ChannelId", "CollectorAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_collector_assignments_CollectorAccountId",
                table: "channel_collector_assignments",
                column: "CollectorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_collector_accounts_ExternalAccountKey",
                table: "collector_accounts",
                column: "ExternalAccountKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fact_check_requests_PostId",
                table: "fact_check_requests",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_fact_check_requests_RequestedByUserId",
                table: "fact_check_requests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_fact_check_requests_Status",
                table: "fact_check_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_post_duplicate_clusters_CanonicalPostId",
                table: "post_duplicate_clusters",
                column: "CanonicalPostId");

            migrationBuilder.CreateIndex(
                name: "IX_post_duplicate_clusters_ClusterKey",
                table: "post_duplicate_clusters",
                column: "ClusterKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_posts_ChannelId_TelegramMessageId",
                table: "telegram_posts",
                columns: new[] { "ChannelId", "TelegramMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_posts_CollectorAccountId",
                table: "telegram_posts",
                column: "CollectorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_posts_ContentHash",
                table: "telegram_posts",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_posts_DuplicateClusterId",
                table: "telegram_posts",
                column: "DuplicateClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_channels_NormalizedChannelKey",
                table: "tracked_channels",
                column: "NormalizedChannelKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracked_channels_TelegramChannelId",
                table: "tracked_channels",
                column: "TelegramChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_user_channel_subscriptions_ChannelId",
                table: "user_channel_subscriptions",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_user_channel_subscriptions_UserId_ChannelId",
                table: "user_channel_subscriptions",
                columns: new[] { "UserId", "ChannelId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_fact_check_requests_telegram_posts_PostId",
                table: "fact_check_requests",
                column: "PostId",
                principalTable: "telegram_posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_post_duplicate_clusters_telegram_posts_CanonicalPostId",
                table: "post_duplicate_clusters",
                column: "CanonicalPostId",
                principalTable: "telegram_posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_telegram_posts_collector_accounts_CollectorAccountId",
                table: "telegram_posts");

            migrationBuilder.DropForeignKey(
                name: "FK_telegram_posts_tracked_channels_ChannelId",
                table: "telegram_posts");

            migrationBuilder.DropForeignKey(
                name: "FK_post_duplicate_clusters_telegram_posts_CanonicalPostId",
                table: "post_duplicate_clusters");

            migrationBuilder.DropTable(
                name: "channel_collector_assignments");

            migrationBuilder.DropTable(
                name: "fact_check_requests");

            migrationBuilder.DropTable(
                name: "user_channel_subscriptions");

            migrationBuilder.DropTable(
                name: "app_users");

            migrationBuilder.DropTable(
                name: "collector_accounts");

            migrationBuilder.DropTable(
                name: "tracked_channels");

            migrationBuilder.DropTable(
                name: "telegram_posts");

            migrationBuilder.DropTable(
                name: "post_duplicate_clusters");
        }
    }
}
