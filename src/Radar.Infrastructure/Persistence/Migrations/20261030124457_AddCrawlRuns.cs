using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Radar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawlRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crawl_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_length = table.Column<long>(type: "bigint", nullable: true),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawl_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_crawl_runs_spec_sources_spec_source_id",
                        column: x => x.spec_source_id,
                        principalTable: "spec_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_crawl_runs_spec_source_id_started_at",
                table: "crawl_runs",
                columns: new[] { "spec_source_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crawl_runs");
        }
    }
}
