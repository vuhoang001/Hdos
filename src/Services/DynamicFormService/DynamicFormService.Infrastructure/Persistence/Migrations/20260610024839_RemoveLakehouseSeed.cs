using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Migrations
{
    // Phase 4 (doc 59): bỏ static seed lakehouse Provider+Operations từng làm bằng
    // HasData() trong commit doc 58. Lakehouse tự push qua gRPC SyncRegistry khi
    // startup. Migration này xóa rows tĩnh nếu có; idempotent (no-op nếu row đã
    // không tồn tại). Down() để trống — không re-seed.
    public partial class RemoveLakehouseSeed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Operations",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                table: "Operations",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"));

            migrationBuilder.DeleteData(
                table: "Providers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
