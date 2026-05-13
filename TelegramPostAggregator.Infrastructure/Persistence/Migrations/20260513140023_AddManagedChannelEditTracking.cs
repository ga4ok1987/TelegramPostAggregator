using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedChannelEditTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TrackPostEdits",
                table: "managed_channels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrackPostEditsEnabledAtUtc",
                table: "managed_channels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "managed_channel_post_trackings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedChannelSubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastDeliveredMessageId = table.Column<long>(type: "bigint", nullable: false),
                    LastDeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TrackEditsUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PendingEditedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastProcessedEditedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_channel_post_trackings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_managed_channel_post_trackings_managed_channel_subscription~",
                        column: x => x.ManagedChannelSubscriptionId,
                        principalTable: "managed_channel_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_managed_channel_post_trackings_managed_channels_ManagedChan~",
                        column: x => x.ManagedChannelId,
                        principalTable: "managed_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_managed_channel_post_trackings_telegram_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "telegram_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_channel_post_trackings_ManagedChannelId",
                table: "managed_channel_post_trackings",
                column: "ManagedChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_managed_channel_post_trackings_ManagedChannelSubscriptionId~",
                table: "managed_channel_post_trackings",
                columns: new[] { "ManagedChannelSubscriptionId", "PostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_channel_post_trackings_PendingEditedAtUtc",
                table: "managed_channel_post_trackings",
                column: "PendingEditedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_managed_channel_post_trackings_PostId",
                table: "managed_channel_post_trackings",
                column: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "managed_channel_post_trackings");

            migrationBuilder.DropColumn(
                name: "TrackPostEdits",
                table: "managed_channels");

            migrationBuilder.DropColumn(
                name: "TrackPostEditsEnabledAtUtc",
                table: "managed_channels");
        }
    }
}
