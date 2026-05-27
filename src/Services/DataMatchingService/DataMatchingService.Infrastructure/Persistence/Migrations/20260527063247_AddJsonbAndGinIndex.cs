using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.DataMatchingService.Infrastructure.Persistence.Migrations
{
    public partial class AddJsonbAndGinIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chuyển kiểu cột từ text → jsonb.
            // USING "CanonicalPayload"::jsonb bắt buộc phải có khi data đã tồn tại:
            // PostgreSQL không tự cast text sang jsonb vì không đảm bảo nội dung là JSON hợp lệ.
            // NULL values vẫn được giữ nguyên (NULL::jsonb = NULL).
            migrationBuilder.Sql(
                """
                ALTER TABLE "StagingRecords"
                ALTER COLUMN "CanonicalPayload" TYPE jsonb
                USING "CanonicalPayload"::jsonb;
                """);

            // Tạo GIN index với jsonb_path_ops — tối ưu cho toán tử @> (containment).
            // jsonb_path_ops nhỏ hơn và nhanh hơn jsonb_ops mặc định cho usecase @>.
            // Query "field=TenKhoa&value=Tim Mach" → {"TenKhoa":"Tim Mach"} @> CanonicalPayload
            // sẽ dùng index này thay vì full table scan.
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_StagingRecords_CanonicalPayload_gin"
                ON "StagingRecords"
                USING GIN ("CanonicalPayload" jsonb_path_ops);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_StagingRecords_CanonicalPayload_gin";""");

            migrationBuilder.Sql(
                """
                ALTER TABLE "StagingRecords"
                ALTER COLUMN "CanonicalPayload" TYPE text
                USING "CanonicalPayload"::text;
                """);
        }
    }
}
