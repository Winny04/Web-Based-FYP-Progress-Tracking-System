using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddDefenseSchedule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DefenseDate",
                table: "FYPMilestone",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefenseTime",
                table: "FYPMilestone",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefenseVenue",
                table: "FYPMilestone",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefenseDate",
                table: "FYPMilestone");

            migrationBuilder.DropColumn(
                name: "DefenseTime",
                table: "FYPMilestone");

            migrationBuilder.DropColumn(
                name: "DefenseVenue",
                table: "FYPMilestone");
        }
    }
}
