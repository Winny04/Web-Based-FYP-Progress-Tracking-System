using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class RemoveUnusedUserColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Department",
                table: "ApplicationUser");

            migrationBuilder.DropColumn(
                name: "OfficeRoom",
                table: "ApplicationUser");

            migrationBuilder.DropColumn(
                name: "ShortName",
                table: "ApplicationUser");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "ApplicationUser",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficeRoom",
                table: "ApplicationUser",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortName",
                table: "ApplicationUser",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
