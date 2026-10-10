using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddAcademicSessionMeetingRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredMeetingsStage1",
                table: "AcademicSession",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredMeetingsStage2",
                table: "AcademicSession",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredMeetingsStage1",
                table: "AcademicSession");

            migrationBuilder.DropColumn(
                name: "RequiredMeetingsStage2",
                table: "AcademicSession");
        }
    }
}
