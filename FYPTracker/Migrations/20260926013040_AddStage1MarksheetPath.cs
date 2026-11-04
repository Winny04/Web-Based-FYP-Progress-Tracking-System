using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddStage1MarksheetPath : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Stage1ExaminerMarksheetPath",
                table: "FYPProject",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Stage1MarksheetUploadCount",
                table: "FYPProject",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Stage1ExaminerMarksheetPath",
                table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "Stage1MarksheetUploadCount",
                table: "FYPProject");
        }
    }
}
