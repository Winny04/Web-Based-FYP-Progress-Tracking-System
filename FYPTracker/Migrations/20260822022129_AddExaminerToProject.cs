using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddExaminerToProject : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExaminerId",
                table: "FYPProject",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExaminerMarksheetPath",
                table: "FYPProject",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FYPProject_ExaminerId",
                table: "FYPProject",
                column: "ExaminerId");

            migrationBuilder.AddForeignKey(
                name: "FK_FYPProject_ApplicationUser_ExaminerId",
                table: "FYPProject",
                column: "ExaminerId",
                principalTable: "ApplicationUser",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FYPProject_ApplicationUser_ExaminerId",
                table: "FYPProject");

            migrationBuilder.DropIndex(
                name: "IX_FYPProject_ExaminerId",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "ExaminerId",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "ExaminerMarksheetPath",
                table: "FYPProject");
        }
    }
}
