using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CertCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generation_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    per_objective = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    objectives_done = table.Column<int>(type: "integer", nullable: false),
                    items_created = table.Column<int>(type: "integer", nullable: false),
                    items_rejected = table.Column<int>(type: "integer", nullable: false),
                    objectives_without_material = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generation_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_generation_jobs_certifications_certification_id",
                        column: x => x.certification_id,
                        principalTable: "certifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generation_jobs_certification_id_status",
                table: "generation_jobs",
                columns: new[] { "certification_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_generation_jobs_status_created_at",
                table: "generation_jobs",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generation_jobs");
        }
    }
}
