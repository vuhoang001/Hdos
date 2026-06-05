using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.LakehouseService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WarehouseSyncStates",
                columns: table => new
                {
                    ViewName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRowCount = table.Column<int>(type: "integer", nullable: false),
                    LastJobId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseSyncStates", x => x.ViewName);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WarehouseSyncStates");
        }
    }
}
