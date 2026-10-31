using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CertCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "review_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    grade = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    correct = table.Column<bool>(type: "boolean", nullable: true),
                    selected_options = table.Column<int[]>(type: "integer[]", nullable: false),
                    was_new = table.Column<bool>(type: "boolean", nullable: false),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_logs_practice_items_item_id",
                        column: x => x.item_id,
                        principalTable: "practice_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_review_logs_certification_id_reviewed_at",
                table: "review_logs",
                columns: new[] { "certification_id", "reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_review_logs_item_id",
                table: "review_logs",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_logs_owner_id_reviewed_on",
                table: "review_logs",
                columns: new[] { "owner_id", "reviewed_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_logs");
        }
    }
}
