using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddChatGroupFeature : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ReceiverId",
                table: "ChatMessage",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupervisorId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatGroup_ApplicationUser_SupervisorId",
                        column: x => x.SupervisorId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatGroupMember",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatGroupMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatGroupMember_ApplicationUser_StudentId",
                        column: x => x.StudentId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatGroupMember_ChatGroup_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ChatGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_GroupId",
                table: "ChatMessage",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGroup_SupervisorId",
                table: "ChatGroup",
                column: "SupervisorId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGroupMember_GroupId",
                table: "ChatGroupMember",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGroupMember_StudentId",
                table: "ChatGroupMember",
                column: "StudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessage_ChatGroup_GroupId",
                table: "ChatMessage",
                column: "GroupId",
                principalTable: "ChatGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessage_ChatGroup_GroupId",
                table: "ChatMessage");

            migrationBuilder.DropTable(
                name: "ChatGroupMember");

            migrationBuilder.DropTable(
                name: "ChatGroup");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_GroupId",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "ChatMessage");

            migrationBuilder.AlterColumn<int>(
                name: "ReceiverId",
                table: "ChatMessage",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
