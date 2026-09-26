using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddMicrosoftGraphIntegration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MeetingLink",
                table: "MeetingBooking",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MsAccessToken",
                table: "ApplicationUser",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MsRefreshToken",
                table: "ApplicationUser",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MsTokenExpiry",
                table: "ApplicationUser",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeetingLink",
                table: "MeetingBooking");

            migrationBuilder.DropColumn(
                name: "MsAccessToken",
                table: "ApplicationUser");

            migrationBuilder.DropColumn(
                name: "MsRefreshToken",
                table: "ApplicationUser");

            migrationBuilder.DropColumn(
                name: "MsTokenExpiry",
                table: "ApplicationUser");
        }
    }
}
