using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CertCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "career_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skills = table.Column<string[]>(type: "text[]", nullable: false),
                    roles = table.Column<string[]>(type: "text[]", nullable: false),
                    held_certifications = table.Column<string[]>(type: "text[]", nullable: false),
                    years_experience = table.Column<int>(type: "integer", nullable: true),
                    target_role = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_career_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_career_profiles_owner_id",
                table: "career_profiles",
                column: "owner_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "career_profiles");
        }
    }
}
