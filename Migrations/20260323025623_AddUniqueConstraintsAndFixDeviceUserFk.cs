using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AeroponicIOT.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueConstraintsAndFixDeviceUserFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_devices_users_UserId1",
                table: "devices");

            migrationBuilder.DropIndex(
                name: "IX_devices_UserId1",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "devices");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "[email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true,
                filter: "[username] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_devices_claim_code",
                table: "devices",
                column: "claim_code",
                unique: true,
                filter: "[claim_code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_devices_mac_address",
                table: "devices",
                column: "mac_address",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_email",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_username",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_devices_claim_code",
                table: "devices");

            migrationBuilder.DropIndex(
                name: "IX_devices_mac_address",
                table: "devices");

            migrationBuilder.AddColumn<int>(
                name: "UserId1",
                table: "devices",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_UserId1",
                table: "devices",
                column: "UserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_devices_users_UserId1",
                table: "devices",
                column: "UserId1",
                principalTable: "users",
                principalColumn: "id");
        }
    }
}
