using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.DataMatchingService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StagingRecords_BusinessKey",
                table: "StagingRecords");

            migrationBuilder.DropIndex(
                name: "IX_SourceProfiles_SourceSystem",
                table: "SourceProfiles");

            migrationBuilder.AddColumn<string>(
                name: "RecordType",
                table: "StagingRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecordType",
                table: "SourceProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_StagingRecords_SourceSystem_RecordType",
                table: "StagingRecords",
                columns: new[] { "SourceSystem", "RecordType" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceProfiles_SourceSystem_RecordType",
                table: "SourceProfiles",
                columns: new[] { "SourceSystem", "RecordType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StagingRecords_SourceSystem_RecordType",
                table: "StagingRecords");

            migrationBuilder.DropIndex(
                name: "IX_SourceProfiles_SourceSystem_RecordType",
                table: "SourceProfiles");

            migrationBuilder.DropColumn(
                name: "RecordType",
                table: "StagingRecords");

            migrationBuilder.DropColumn(
                name: "RecordType",
                table: "SourceProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_StagingRecords_BusinessKey",
                table: "StagingRecords",
                column: "BusinessKey");

            migrationBuilder.CreateIndex(
                name: "IX_SourceProfiles_SourceSystem",
                table: "SourceProfiles",
                column: "SourceSystem",
                unique: true);
        }
    }
}
