using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddParentMeetingBookingId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentMeetingBookingId",
                table: "MeetingBooking",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeetingBooking_ParentMeetingBookingId_WeekNumber",
                table: "MeetingBooking",
                columns: new[] { "ParentMeetingBookingId", "WeekNumber" },
                unique: true,
                filter: "[ParentMeetingBookingId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MeetingBooking_ParentMeetingBookingId_WeekNumber",
                table: "MeetingBooking");

            migrationBuilder.DropColumn(
                name: "ParentMeetingBookingId",
                table: "MeetingBooking");
        }
    }
}
