using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddJointMarkingFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExaminerMarksheetData",
                table: "FYPMilestone",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReadyForJointEvaluation",
                table: "FYPMilestone",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SupervisorMarksheetData",
                table: "FYPMilestone",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExaminerMarksheetData",
                table: "FYPMilestone");

            migrationBuilder.DropColumn(
                name: "IsReadyForJointEvaluation",
                table: "FYPMilestone");

            migrationBuilder.DropColumn(
                name: "SupervisorMarksheetData",
                table: "FYPMilestone");
        }
    }
}
