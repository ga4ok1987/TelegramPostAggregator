using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedChannelSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "managed_channel_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastDeliveredTelegramMessageId = table.Column<long>(type: "bigint", nullable: true),
                    LastDeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_channel_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_managed_channel_subscriptions_managed_channels_ManagedChann~",
                        column: x => x.ManagedChannelId,
                        principalTable: "managed_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_managed_channel_subscriptions_tracked_channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "tracked_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_channel_subscriptions_ChannelId",
                table: "managed_channel_subscriptions",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_managed_channel_subscriptions_IsActive_LastDeliveredTelegra~",
                table: "managed_channel_subscriptions",
                columns: new[] { "IsActive", "LastDeliveredTelegramMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_managed_channel_subscriptions_ManagedChannelId_ChannelId",
                table: "managed_channel_subscriptions",
                columns: new[] { "ManagedChannelId", "ChannelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "managed_channel_subscriptions");
        }
    }
}
