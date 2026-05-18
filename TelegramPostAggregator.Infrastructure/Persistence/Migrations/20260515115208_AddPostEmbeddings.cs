using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPostAggregator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbeddingLastError",
                table: "telegram_posts",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "telegram_posts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingStatus",
                table: "telegram_posts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingTextVersion",
                table: "telegram_posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmbeddingUpdatedAtUtc",
                table: "telegram_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "embedding_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "telegram_post_embeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TextVersion = table.Column<int>(type: "integer", nullable: false),
                    NormalizedText = table.Column<string>(type: "text", nullable: false),
                    VectorJson = table.Column<string>(type: "jsonb", nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_post_embeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_post_embeddings_telegram_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "telegram_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_posts_EmbeddingStatus_PublishedAtUtc",
                table: "telegram_posts",
                columns: new[] { "EmbeddingStatus", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_post_embeddings_ExpiresAtUtc",
                table: "telegram_post_embeddings",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_post_embeddings_PostId",
                table: "telegram_post_embeddings",
                column: "PostId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embedding_settings");

            migrationBuilder.DropTable(
                name: "telegram_post_embeddings");

            migrationBuilder.DropIndex(
                name: "IX_telegram_posts_EmbeddingStatus_PublishedAtUtc",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "EmbeddingLastError",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "EmbeddingStatus",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "EmbeddingTextVersion",
                table: "telegram_posts");

            migrationBuilder.DropColumn(
                name: "EmbeddingUpdatedAtUtc",
                table: "telegram_posts");
        }
    }
}
