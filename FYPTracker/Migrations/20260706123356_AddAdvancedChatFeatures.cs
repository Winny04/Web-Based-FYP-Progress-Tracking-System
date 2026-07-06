using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddAdvancedChatFeatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "ChatMessage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReplyToId",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ReplyToId",
                table: "ChatMessage",
                column: "ReplyToId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessage_ChatMessage_ReplyToId",
                table: "ChatMessage",
                column: "ReplyToId",
                principalTable: "ChatMessage",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessage_ChatMessage_ReplyToId",
                table: "ChatMessage");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_ReplyToId",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "ReplyToId",
                table: "ChatMessage");
        }
    }
}
