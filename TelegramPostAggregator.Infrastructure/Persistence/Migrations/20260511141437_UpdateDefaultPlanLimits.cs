using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDefaultPlanLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE subscription_plan_definitions
                SET "ChannelLimit" = CASE "Code"
                    WHEN 'free' THEN 1
                    WHEN 'basic' THEN 3
                    WHEN 'pro' THEN 8
                    WHEN 'business' THEN 12
                    WHEN 'business-plus-plus' THEN 15
                    ELSE "ChannelLimit"
                END,
                "UpdatedAtUtc" = NOW()
                WHERE "Code" IN ('free', 'basic', 'pro', 'business', 'business-plus-plus');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE subscription_plan_definitions
                SET "ChannelLimit" = CASE "Code"
                    WHEN 'free' THEN 10
                    WHEN 'basic' THEN 20
                    WHEN 'pro' THEN 50
                    WHEN 'business' THEN 150
                    WHEN 'business-plus-plus' THEN 250
                    ELSE "ChannelLimit"
                END,
                "UpdatedAtUtc" = NOW()
                WHERE "Code" IN ('free', 'basic', 'pro', 'business', 'business-plus-plus');
                """);
        }
    }
}
