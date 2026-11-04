using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddStage1GradeAndStage2Marksheet : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Stage1Grade",
                table: "FYPProject",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage2ExaminerMarksheetData",
                table: "FYPProject",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Stage1Grade",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "Stage2ExaminerMarksheetData",
                table: "FYPProject");
        }
    }
}
