using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerUserSubscriptionAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExtraSubscriptionSlots",
                table: "app_users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraSubscriptionSlots",
                table: "app_users");
        }
    }
}
