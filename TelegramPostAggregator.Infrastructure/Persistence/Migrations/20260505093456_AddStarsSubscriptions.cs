using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStarsSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastStarsPaymentAtUtc",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubscriptionExpiresAtUtc",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionPlanCode",
                table: "app_users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "free");

            migrationBuilder.CreateTable(
                name: "donation_options",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StarsAmount = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donation_options", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_payment_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DonationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TotalAmountStars = table.Column<int>(type: "integer", nullable: false),
                    TelegramPaymentChargeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PreCheckoutApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_payment_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscription_payment_transactions_app_users_UserId",
                        column: x => x.UserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plan_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChannelLimit = table.Column<int>(type: "integer", nullable: false),
                    PriceStars = table.Column<int>(type: "integer", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefaultPlan = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plan_definitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_donation_options_Code",
                table: "donation_options",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_transactions_PayloadToken",
                table: "subscription_payment_transactions",
                column: "PayloadToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_transactions_TelegramPaymentChargeId",
                table: "subscription_payment_transactions",
                column: "TelegramPaymentChargeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_transactions_UserId",
                table: "subscription_payment_transactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plan_definitions_Code",
                table: "subscription_plan_definitions",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "donation_options");

            migrationBuilder.DropTable(
                name: "subscription_payment_transactions");

            migrationBuilder.DropTable(
                name: "subscription_plan_definitions");

            migrationBuilder.DropColumn(
                name: "LastStarsPaymentAtUtc",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "SubscriptionExpiresAtUtc",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanCode",
                table: "app_users");
        }
    }
}
