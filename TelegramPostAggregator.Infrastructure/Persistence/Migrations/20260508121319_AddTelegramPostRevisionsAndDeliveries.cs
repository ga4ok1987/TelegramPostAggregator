using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramPostRevisionsAndDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "telegram_posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TelegramEditDateUtc",
                table: "telegram_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "telegram_post_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    DestinationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DestinationChatId = table.Column<long>(type: "bigint", nullable: false),
                    DeliveredTelegramMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_post_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_post_deliveries_telegram_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "telegram_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_post_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    IsEdited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TelegramEditDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawText = table.Column<string>(type: "text", nullable: false),
                    NormalizedText = table.Column<string>(type: "text", nullable: false),
                    MediaGroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    HasMedia = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalPostUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_post_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_post_revisions_telegram_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "telegram_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_post_deliveries_DestinationKind_DestinationChatId_~",
                table: "telegram_post_deliveries",
                columns: new[] { "DestinationKind", "DestinationChatId", "DeliveredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_post_deliveries_PostId_DestinationKind_Destination~",
                table: "telegram_post_deliveries",
                columns: new[] { "PostId", "DestinationKind", "DestinationChatId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_post_revisions_PostId_RevisionNumber",
                table: "telegram_post_revisions",
                columns: new[] { "PostId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_post_deliveries");

            migrationBuilder.DropTable(
                name: "telegram_post_revisions");

            migrationBuilder.DropColumn(
                name: "IsEdited",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "TelegramEditDateUtc",
                table: "telegram_posts");
        }
    }
}
