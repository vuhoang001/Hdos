using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.LakehouseService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LakehouseSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Namespace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    JobId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LakehouseSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LakehouseSnapshots_JobId",
                table: "LakehouseSnapshots",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_LakehouseSnapshots_Namespace_BusinessKey",
                table: "LakehouseSnapshots",
                columns: new[] { "Namespace", "BusinessKey" });

            migrationBuilder.CreateIndex(
                name: "IX_LakehouseSnapshots_ReceivedAt",
                table: "LakehouseSnapshots",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LakehouseSnapshots");
        }
    }
}
