using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormPages");

            migrationBuilder.CreateTable(
                name: "FormScreens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormScreens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormScreenTabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScreenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormScreenTabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormScreenTabs_FormScreens_ScreenId",
                        column: x => x.ScreenId,
                        principalTable: "FormScreens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormScreenWidgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TabId = table.Column<Guid>(type: "uuid", nullable: false),
                    WidgetKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WidgetType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GridX = table.Column<int>(type: "integer", nullable: false),
                    GridY = table.Column<int>(type: "integer", nullable: false),
                    GridW = table.Column<int>(type: "integer", nullable: false),
                    GridH = table.Column<int>(type: "integer", nullable: false),
                    ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormScreenWidgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormScreenWidgets_FormScreenTabs_TabId",
                        column: x => x.TabId,
                        principalTable: "FormScreenTabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormScreens_ModuleCode",
                table: "FormScreens",
                column: "ModuleCode");

            migrationBuilder.CreateIndex(
                name: "IX_FormScreens_ModuleCode_Code",
                table: "FormScreens",
                columns: new[] { "ModuleCode", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormScreens_Status",
                table: "FormScreens",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FormScreenTabs_ScreenId",
                table: "FormScreenTabs",
                column: "ScreenId");

            migrationBuilder.CreateIndex(
                name: "IX_FormScreenTabs_ScreenId_Slug",
                table: "FormScreenTabs",
                columns: new[] { "ScreenId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormScreenWidgets_TabId",
                table: "FormScreenWidgets",
                column: "TabId");

            migrationBuilder.CreateIndex(
                name: "IX_FormScreenWidgets_TabId_WidgetKey",
                table: "FormScreenWidgets",
                columns: new[] { "TabId", "WidgetKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormScreenWidgets");

            migrationBuilder.DropTable(
                name: "FormScreenTabs");

            migrationBuilder.DropTable(
                name: "FormScreens");

            migrationBuilder.CreateTable(
                name: "FormPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LayoutJson = table.Column<string>(type: "jsonb", nullable: false),
                    ModuleCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormPages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormPages_ModuleCode",
                table: "FormPages",
                column: "ModuleCode");

            migrationBuilder.CreateIndex(
                name: "IX_FormPages_ModuleCode_Code",
                table: "FormPages",
                columns: new[] { "ModuleCode", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormPages_Status",
                table: "FormPages",
                column: "Status");
        }
    }
}
