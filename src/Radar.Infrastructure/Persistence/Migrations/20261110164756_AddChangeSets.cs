using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Radar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "change_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    from_api_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    to_api_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    breaking_count = table.Column<int>(type: "integer", nullable: false),
                    warning_count = table.Column<int>(type: "integer", nullable: false),
                    info_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_sets", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_sets_spec_snapshots_from_snapshot_id",
                        column: x => x.from_snapshot_id,
                        principalTable: "spec_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_change_sets_spec_snapshots_to_snapshot_id",
                        column: x => x.to_snapshot_id,
                        principalTable: "spec_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_change_sets_spec_sources_spec_source_id",
                        column: x => x.spec_source_id,
                        principalTable: "spec_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    severity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_changes_change_sets_change_set_id",
                        column: x => x.change_set_id,
                        principalTable: "change_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_changes_change_set_id_position",
                table: "api_changes",
                columns: new[] { "change_set_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_change_sets_from_snapshot_id",
                table: "change_sets",
                column: "from_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_sets_spec_source_id_detected_at",
                table: "change_sets",
                columns: new[] { "spec_source_id", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_change_sets_to_snapshot_id",
                table: "change_sets",
                column: "to_snapshot_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_changes");

            migrationBuilder.DropTable(
                name: "change_sets");
        }
    }
}
