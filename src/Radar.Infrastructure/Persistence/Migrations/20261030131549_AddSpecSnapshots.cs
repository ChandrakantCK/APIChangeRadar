using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Radar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spec_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crawl_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    spec_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    api_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    canonical_length = table.Column<long>(type: "bigint", nullable: false),
                    compressed_content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_spec_snapshots_crawl_runs_crawl_run_id",
                        column: x => x.crawl_run_id,
                        principalTable: "crawl_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_spec_snapshots_spec_sources_spec_source_id",
                        column: x => x.spec_source_id,
                        principalTable: "spec_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_spec_snapshots_crawl_run_id",
                table: "spec_snapshots",
                column: "crawl_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_spec_snapshots_spec_source_id_captured_at",
                table: "spec_snapshots",
                columns: new[] { "spec_source_id", "captured_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spec_snapshots");
        }
    }
}
