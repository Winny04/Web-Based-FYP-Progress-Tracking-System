using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class RemoveLegacyTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditTag");

            migrationBuilder.DropTable(
                name: "EmailTemplateConfig");

            migrationBuilder.DropTable(
                name: "AuditTrail");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditTrail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpId = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataAfter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataBefore = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FullMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogLevelId = table.Column<int>(type: "int", nullable: false),
                    PageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferrerUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShortMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TableChange = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditTrail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditTrail_ApplicationUser_EmpId",
                        column: x => x.EmpId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplateConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfigValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequireParams = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplateConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditTag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuditId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    CreateAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditTag", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditTag_ApplicationUser_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuditTag_AuditTrail_AuditId",
                        column: x => x.AuditId,
                        principalTable: "AuditTrail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditTag_AuditId",
                table: "AuditTag",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditTag_EmployeeId",
                table: "AuditTag",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditTrail_EmpId",
                table: "AuditTrail",
                column: "EmpId");
        }
    }
}
