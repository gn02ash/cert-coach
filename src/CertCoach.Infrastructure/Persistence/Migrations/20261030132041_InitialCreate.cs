using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CertCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "certifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    vendor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    replaced_by_exam_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    outline_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    outline_version_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exam_domains",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    certification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    weight_min_percent = table.Column<int>(type: "integer", nullable: false),
                    weight_max_percent = table.Column<int>(type: "integer", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_domains", x => x.id);
                    table.CheckConstraint("ck_exam_domains_weight_range", "weight_min_percent >= 0 AND weight_max_percent <= 100 AND weight_min_percent <= weight_max_percent");
                    table.ForeignKey(
                        name: "fk_exam_domains_certifications_certification_id",
                        column: x => x.certification_id,
                        principalTable: "certifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    exam_date = table.Column<DateOnly>(type: "date", nullable: false),
                    hours_per_week = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    study_days = table.Column<int>(type: "integer", nullable: false),
                    review_buffer_percent = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    shortfall_missing_sessions = table.Column<int>(type: "integer", nullable: true),
                    shortfall_suggested_exam_date = table.Column<DateOnly>(type: "date", nullable: true),
                    shortfall_suggested_hours_per_week = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_study_plans_certifications_certification_id",
                        column: x => x.certification_id,
                        principalTable: "certifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skill_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skill_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_skill_areas_exam_domains_domain_id",
                        column: x => x.domain_id,
                        principalTable: "exam_domains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    focus_domain_id = table.Column<Guid>(type: "uuid", nullable: true),
                    objective_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_study_sessions_study_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "study_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objectives", x => x.id);
                    table.ForeignKey(
                        name: "fk_objectives_skill_area_skill_area_id",
                        column: x => x.skill_area_id,
                        principalTable: "skill_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_certifications_owner_id_exam_code",
                table: "certifications",
                columns: new[] { "owner_id", "exam_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_domains_certification_id",
                table: "exam_domains",
                column: "certification_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_skill_area_id",
                table: "objectives",
                column: "skill_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_skill_areas_domain_id",
                table: "skill_areas",
                column: "domain_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_plans_certification_id",
                table: "study_plans",
                column: "certification_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_plans_owner_id_status",
                table: "study_plans",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_study_sessions_plan_id_scheduled_date",
                table: "study_sessions",
                columns: new[] { "plan_id", "scheduled_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "objectives");

            migrationBuilder.DropTable(
                name: "study_sessions");

            migrationBuilder.DropTable(
                name: "skill_areas");

            migrationBuilder.DropTable(
                name: "study_plans");

            migrationBuilder.DropTable(
                name: "exam_domains");

            migrationBuilder.DropTable(
                name: "certifications");
        }
    }
}
