using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace CertCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    has_custom_title = table.Column<bool>(type: "boolean", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    certification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    chunk_count = table.Column<int>(type: "integer", nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_certifications_certification_id",
                        column: x => x.certification_id,
                        principalTable: "certifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    heading_path = table.Column<string>(type: "text", nullable: false),
                    page_start = table.Column<int>(type: "integer", nullable: false),
                    page_end = table.Column<int>(type: "integer", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('english', heading_path || ' ' || text)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_chunks_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_document_id_index",
                table: "document_chunks",
                columns: new[] { "document_id", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_embedding",
                table: "document_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_owner_id",
                table: "document_chunks",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_search_vector",
                table: "document_chunks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_documents_certification_id",
                table: "documents",
                column: "certification_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner_id_content_hash",
                table: "documents",
                columns: new[] { "owner_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_status_next_attempt_at",
                table: "documents",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
