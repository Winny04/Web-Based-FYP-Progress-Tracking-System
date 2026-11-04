using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddMeetingProjectStage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FYPProjectId",
                table: "MeetingBooking",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "MeetingBooking",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeekNumber",
                table: "MeetingBooking",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FYPProjectId",
                table: "MeetingBooking");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "MeetingBooking");

            migrationBuilder.DropColumn(
                name: "WeekNumber",
                table: "MeetingBooking");
        }
    }
}
