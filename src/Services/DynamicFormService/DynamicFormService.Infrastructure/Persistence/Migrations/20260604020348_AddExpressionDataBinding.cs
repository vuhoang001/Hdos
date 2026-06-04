using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpressionDataBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataSourcesJson",
                table: "FormScreens",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataBindingJson",
                table: "FormFields",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "FormFields",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataSourcesJson",
                table: "FormScreens");

            migrationBuilder.DropColumn(
                name: "DataBindingJson",
                table: "FormFields");

            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "FormFields");
        }
    }
}
