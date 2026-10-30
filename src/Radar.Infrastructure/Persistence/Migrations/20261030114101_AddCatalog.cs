using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Radar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    website_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "spec_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    spec_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_spec_sources_api_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "api_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "api_providers",
                columns: new[] { "id", "name", "slug", "website_url" },
                values: new object[,]
                {
                    { new Guid("528c303d-c68d-4834-80a6-cdc860f74b43"), "Stripe", "stripe", "https://stripe.com" },
                    { new Guid("8cd22e47-268b-4e0d-891b-f9cfd327af2d"), "Twilio", "twilio", "https://www.twilio.com" },
                    { new Guid("d5b42dc0-59b2-4ec4-8dff-bbe4446a7c54"), "GitHub", "github", "https://github.com" }
                });

            migrationBuilder.InsertData(
                table: "spec_sources",
                columns: new[] { "id", "branch", "file_path", "format", "is_active", "name", "provider_id", "repository", "slug", "spec_url" },
                values: new object[,]
                {
                    { new Guid("42c4b303-6db2-48d1-bf97-304feab0ad8d"), "master", "openapi/spec3.json", "OpenApi", true, "Stripe API", new Guid("528c303d-c68d-4834-80a6-cdc860f74b43"), "stripe/openapi", "api", "https://raw.githubusercontent.com/stripe/openapi/master/openapi/spec3.json" },
                    { new Guid("5cba65a3-c7c2-494d-ae65-93ccfa284438"), "main", "descriptions/api.github.com/api.github.com.json", "OpenApi", true, "GitHub REST API", new Guid("d5b42dc0-59b2-4ec4-8dff-bbe4446a7c54"), "github/rest-api-description", "rest-api", "https://raw.githubusercontent.com/github/rest-api-description/main/descriptions/api.github.com/api.github.com.json" },
                    { new Guid("cdec6a40-8dbb-4bdc-98a5-a755c9490064"), "main", "spec/json/twilio_api_v2010.json", "OpenApi", true, "Twilio API (2010-04-01)", new Guid("8cd22e47-268b-4e0d-891b-f9cfd327af2d"), "twilio/twilio-oai", "api-v2010", "https://raw.githubusercontent.com/twilio/twilio-oai/main/spec/json/twilio_api_v2010.json" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_providers_slug",
                table: "api_providers",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_spec_sources_provider_id_slug",
                table: "spec_sources",
                columns: new[] { "provider_id", "slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spec_sources");

            migrationBuilder.DropTable(
                name: "api_providers");
        }
    }
}
