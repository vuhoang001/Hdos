using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.M01Service.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BenhNhans",
                columns: table => new
                {
                    MaBn = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    HoTen = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Triage = table.Column<int>(type: "int", nullable: false),
                    TrangThai = table.Column<int>(type: "int", nullable: false),
                    MaPhongKham = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BacSi = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ChoPhut = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenhNhans", x => x.MaBn);
                });

            migrationBuilder.CreateTable(
                name: "CapCuus",
                columns: table => new
                {
                    MaCu = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    HoTen = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Triage = table.Column<int>(type: "int", nullable: false),
                    ChoPhut = table.Column<int>(type: "int", nullable: false),
                    BacSi = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TrangThai = table.Column<int>(type: "int", nullable: false),
                    CanhBao = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapCuus", x => x.MaCu);
                });

            migrationBuilder.CreateTable(
                name: "DashboardSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    TongLuotKham = table.Column<int>(type: "int", nullable: false),
                    ChoKhamTbPhut = table.Column<int>(type: "int", nullable: false),
                    ChoMaxPhut = table.Column<int>(type: "int", nullable: false),
                    TriageP1 = table.Column<int>(type: "int", nullable: false),
                    TriageP2 = table.Column<int>(type: "int", nullable: false),
                    TriageP3 = table.Column<int>(type: "int", nullable: false),
                    TrongNguong = table.Column<bool>(type: "bit", nullable: false),
                    FlowDangKy = table.Column<int>(type: "int", nullable: false),
                    FlowChoKham = table.Column<int>(type: "int", nullable: false),
                    FlowDangKham = table.Column<int>(type: "int", nullable: false),
                    FlowChoCls = table.Column<int>(type: "int", nullable: false),
                    FlowNhanKq = table.Column<int>(type: "int", nullable: false),
                    FlowKeDonNv = table.Column<int>(type: "int", nullable: false),
                    FlowHoanThanh = table.Column<int>(type: "int", nullable: false),
                    FlowTatTbPhut = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForecastEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Gio = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DuBao = table.Column<int>(type: "int", nullable: false),
                    ThucTe = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForecastMetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CaoDiemDuKien = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DoChinhXacMae = table.Column<double>(type: "float", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastMetas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceiveCount = table.Column<int>(type: "int", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Consumed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "KhoaDoanhThus",
                columns: table => new
                {
                    MaKhoa = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TenKhoa = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SoBenhNhan = table.Column<int>(type: "int", nullable: false),
                    TongThu = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BhytTra = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BnTra = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    HaoPhiKhac = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NgayBaoCao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KhoaDoanhThus", x => x.MaKhoa);
                });

            migrationBuilder.CreateTable(
                name: "NhanSuTrucs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    HoTen = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Khoa = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MaPhongKham = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SoBnDangKham = table.Column<int>(type: "int", nullable: false),
                    TrangThai = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NhanSuTrucs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnqueueTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "PhongKhams",
                columns: table => new
                {
                    MaPhong = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TenPhong = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ChoTbPhut = table.Column<int>(type: "int", nullable: false),
                    SoBenhNhan = table.Column<int>(type: "int", nullable: false),
                    MucDoTai = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhongKhams", x => x.MaPhong);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenhNhans_MaPhongKham",
                table: "BenhNhans",
                column: "MaPhongKham");

            migrationBuilder.CreateIndex(
                name: "IX_BenhNhans_TrangThai",
                table: "BenhNhans",
                column: "TrangThai");

            migrationBuilder.CreateIndex(
                name: "IX_ForecastEntries_Gio",
                table: "ForecastEntries",
                column: "Gio");

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_KhoaDoanhThus_NgayBaoCao",
                table: "KhoaDoanhThus",
                column: "NgayBaoCao");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true,
                filter: "[InboxMessageId] IS NOT NULL AND [InboxConsumerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true,
                filter: "[OutboxId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenhNhans");

            migrationBuilder.DropTable(
                name: "CapCuus");

            migrationBuilder.DropTable(
                name: "DashboardSnapshots");

            migrationBuilder.DropTable(
                name: "ForecastEntries");

            migrationBuilder.DropTable(
                name: "ForecastMetas");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "KhoaDoanhThus");

            migrationBuilder.DropTable(
                name: "NhanSuTrucs");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "PhongKhams");
        }
    }
}
