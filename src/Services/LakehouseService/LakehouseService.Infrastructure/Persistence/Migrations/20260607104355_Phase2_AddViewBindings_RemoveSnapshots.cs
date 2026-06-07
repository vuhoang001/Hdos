using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.LakehouseService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_AddViewBindings_RemoveSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LakehouseSnapshots");

            migrationBuilder.CreateTable(
                name: "ViewBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessKeyColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAtColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PollIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewBindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ViewBindings_SourceSystem_RecordType",
                table: "ViewBindings",
                columns: new[] { "SourceSystem", "RecordType" });

            migrationBuilder.CreateIndex(
                name: "IX_ViewBindings_ViewName",
                table: "ViewBindings",
                column: "ViewName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ViewBindings");

            migrationBuilder.CreateTable(
                name: "LakehouseSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    JobId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Namespace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
    }
}
