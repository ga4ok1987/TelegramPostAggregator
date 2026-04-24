using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionDeliveryCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastDeliveredAtUtc",
                table: "user_channel_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastDeliveredTelegramMessageId",
                table: "user_channel_subscriptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_channel_subscriptions_IsActive_LastDeliveredTelegramMe~",
                table: "user_channel_subscriptions",
                columns: new[] { "IsActive", "LastDeliveredTelegramMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_channel_subscriptions_IsActive_LastDeliveredTelegramMe~",
                table: "user_channel_subscriptions");

            migrationBuilder.DropColumn(
                name: "LastDeliveredAtUtc",
                table: "user_channel_subscriptions");

            migrationBuilder.DropColumn(
                name: "LastDeliveredTelegramMessageId",
                table: "user_channel_subscriptions");
        }
    }
}
