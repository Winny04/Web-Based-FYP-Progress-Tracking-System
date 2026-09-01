using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddProjectExaminerMapping : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FYPProject_ApplicationUser_ExaminerId",
                table: "FYPProject");

            // migrationBuilder.DropIndex(
            //    name: "IX_FYPProject_ExaminerId",
            //    table: "FYPProject");

            migrationBuilder.DropColumn(
                name: "ExaminerId",
                table: "FYPProject");

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "FYPSubmissionFile",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FYPProjectExaminer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FYPProjectId = table.Column<int>(type: "int", nullable: false),
                    ExaminerId = table.Column<int>(type: "int", nullable: false),
                    AssignedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FYPProjectExaminer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FYPProjectExaminer_ApplicationUser_ExaminerId",
                        column: x => x.ExaminerId,
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FYPProjectExaminer_FYPProject_FYPProjectId",
                        column: x => x.FYPProjectId,
                        principalTable: "FYPProject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FYPProjectExaminer_ExaminerId",
                table: "FYPProjectExaminer",
                column: "ExaminerId");

            migrationBuilder.CreateIndex(
                name: "IX_FYPProjectExaminer_FYPProjectId",
                table: "FYPProjectExaminer",
                column: "FYPProjectId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FYPProjectExaminer");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "FYPSubmissionFile");

            migrationBuilder.AddColumn<int>(
                name: "ExaminerId",
                table: "FYPProject",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FYPProject_ExaminerId",
                table: "FYPProject",
                column: "ExaminerId");

            migrationBuilder.AddForeignKey(
                name: "FK_FYPProject_ApplicationUser_ExaminerId",
                table: "FYPProject",
                column: "ExaminerId",
                principalTable: "ApplicationUser",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
