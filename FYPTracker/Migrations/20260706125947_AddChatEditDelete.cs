using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddChatEditDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ChatMessage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "ChatMessage",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "IsEdited",
                table: "ChatMessage");
        }
    }
}
