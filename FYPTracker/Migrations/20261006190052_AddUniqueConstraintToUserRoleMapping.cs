using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FYPTracker.Migrations
{
    public partial class AddUniqueConstraintToUserRoleMapping : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRoleMappings_UserId",
                table: "UserRoleMappings");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleMappings_UserId_RoleId",
                table: "UserRoleMappings",
                columns: new[] { "UserId", "RoleId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRoleMappings_UserId_RoleId",
                table: "UserRoleMappings");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleMappings_UserId",
                table: "UserRoleMappings",
                column: "UserId");
        }
    }
}
