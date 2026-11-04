using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddStudentInvite : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExaminerComments",
                table: "FYPProject",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExaminerImprovements",
                table: "FYPProject",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExaminerStrengths",
                table: "FYPProject",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTurnitinApproved",
                table: "FYPProject",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TurnitinScore",
                table: "FYPProject",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AccountExpiry",
                table: "ApplicationUser",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TempPassword",
                table: "ApplicationUser",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvitationLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: true),
                    StudentEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AdminId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitationLog_ApplicationUser_AdminId",
                        column: x => x.AdminId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvitationLog_ApplicationUser_StudentId",
                        column: x => x.StudentId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationLog_AdminId",
                table: "InvitationLog",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationLog_StudentId",
                table: "InvitationLog",
                column: "StudentId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvitationLog");

            migrationBuilder.DropColumn(
                name: "ExaminerComments",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "ExaminerImprovements",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "ExaminerStrengths",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "IsTurnitinApproved",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "TurnitinScore",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "AccountExpiry",
                table: "ApplicationUser");

            migrationBuilder.DropColumn(
                name: "TempPassword",
                table: "ApplicationUser");
        }
    }
}
