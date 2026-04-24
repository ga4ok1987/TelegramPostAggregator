using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePostDeduplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_telegram_posts_post_duplicate_clusters_DuplicateClusterId",
                table: "telegram_posts");

            migrationBuilder.DropTable(
                name: "post_duplicate_clusters");

            migrationBuilder.DropIndex(
                name: "IX_telegram_posts_ContentHash",
                table: "telegram_posts");

            migrationBuilder.DropIndex(
                name: "IX_telegram_posts_DuplicateClusterId",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "DuplicateClusterId",
                table: "telegram_posts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "telegram_posts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateClusterId",
                table: "telegram_posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "post_duplicate_clusters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalPostId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClusterKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    SummaryNormalizedText = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_duplicate_clusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_post_duplicate_clusters_telegram_posts_CanonicalPostId",
                        column: x => x.CanonicalPostId,
                        principalTable: "telegram_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_posts_ContentHash",
                table: "telegram_posts",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_posts_DuplicateClusterId",
                table: "telegram_posts",
                column: "DuplicateClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_post_duplicate_clusters_CanonicalPostId",
                table: "post_duplicate_clusters",
                column: "CanonicalPostId");

            migrationBuilder.CreateIndex(
                name: "IX_post_duplicate_clusters_ClusterKey",
                table: "post_duplicate_clusters",
                column: "ClusterKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_telegram_posts_post_duplicate_clusters_DuplicateClusterId",
                table: "telegram_posts",
                column: "DuplicateClusterId",
                principalTable: "post_duplicate_clusters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
