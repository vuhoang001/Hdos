using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hdos.AuthService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitRbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Users table: create on fresh install OR migrate from old Init schema
            // (old Init had PasswordHash + LastLoginUtc; new schema has LastSeenUtc instead)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
                BEGIN
                    CREATE TABLE [Users] (
                        [Id]           uniqueidentifier NOT NULL,
                        [Email]        nvarchar(255)    NOT NULL,
                        [FullName]     nvarchar(120)    NOT NULL,
                        [LastSeenUtc]  datetime2        NULL,
                        [CreatedAtUtc] datetime2        NOT NULL,
                        [UpdatedAtUtc] datetime2        NULL,
                        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
                END
                ELSE
                BEGIN
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'PasswordHash')
                        ALTER TABLE [Users] DROP COLUMN [PasswordHash];

                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                               WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'LastLoginUtc')
                        ALTER TABLE [Users] DROP COLUMN [LastLoginUtc];

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                                   WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'LastSeenUtc')
                        ALTER TABLE [Users] ADD [LastSeenUtc] datetime2 NULL;

                    IF NOT EXISTS (SELECT * FROM sys.indexes
                                   WHERE name = 'IX_Users_Email'
                                     AND object_id = OBJECT_ID('Users'))
                        CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
                END
            ");

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Resource_Action",
                table: "Permissions",
                columns: new[] { "Resource", "Action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RolePermissions");
            migrationBuilder.DropTable(name: "UserRoles");
            migrationBuilder.DropTable(name: "Permissions");
            migrationBuilder.DropTable(name: "Roles");
            migrationBuilder.DropTable(name: "Users");
        }
    }
}
