using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddMeetingLog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeetingLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeetingBookingId = table.Column<int>(type: "int", nullable: false),
                    KeyTopicsDiscussed = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionPointsForNextMeeting = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedActionsAndNotes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupervisorComments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NextMeetingDate = table.Column<DateTime>(type: "date", nullable: true),
                    SignatureDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LogStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingLog_MeetingBooking_MeetingBookingId",
                        column: x => x.MeetingBookingId,
                        principalTable: "MeetingBooking",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingLog_MeetingBookingId",
                table: "MeetingLog",
                column: "MeetingBookingId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeetingLog");
        }
    }
}
