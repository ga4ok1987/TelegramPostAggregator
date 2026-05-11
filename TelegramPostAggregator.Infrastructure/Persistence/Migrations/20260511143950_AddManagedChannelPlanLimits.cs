using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedChannelPlanLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ManagedChannelLimit",
                table: "subscription_plan_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ExtraManagedChannelSlots",
                table: "app_users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE subscription_plan_definitions
                SET "ManagedChannelLimit" = CASE "Code"
                    WHEN 'free' THEN 1
                    WHEN 'basic' THEN 3
                    WHEN 'pro' THEN 8
                    WHEN 'business' THEN 12
                    WHEN 'business-plus-plus' THEN 15
                    ELSE "ManagedChannelLimit"
                END,
                "UpdatedAtUtc" = NOW()
                WHERE "Code" IN ('free', 'basic', 'pro', 'business', 'business-plus-plus');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagedChannelLimit",
                table: "subscription_plan_definitions");

            migrationBuilder.DropColumn(
                name: "ExtraManagedChannelSlots",
                table: "app_users");
        }
    }
}
