using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Accounts_Id",
                table: "Accounts",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Id",
                table: "Accounts",
                column: "Id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Profiles_Accounts_UserId",
                table: "Profiles",
                column: "UserId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Profiles_Accounts_UserId",
                table: "Profiles");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Accounts_Id",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Id",
                table: "Accounts");
        }
    }
}
