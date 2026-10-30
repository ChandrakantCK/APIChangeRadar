using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Radar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "watches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watches", x => x.id);
                    table.ForeignKey(
                        name: "fk_watches_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_watches_spec_sources_spec_source_id",
                        column: x => x.spec_source_id,
                        principalTable: "spec_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watches_organization_id_spec_source_id",
                table: "watches",
                columns: new[] { "organization_id", "spec_source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_watches_spec_source_id",
                table: "watches",
                column: "spec_source_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watches");
        }
    }
}
