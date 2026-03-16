using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AeroponicIOT.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceProvisioningFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "chip_id",
                table: "devices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "claim_code",
                table: "devices",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "claim_code_expires_at",
                table: "devices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "firmware_version",
                table: "devices",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "provisioned_at",
                table: "devices",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chip_id",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "claim_code",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "claim_code_expires_at",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "firmware_version",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "provisioned_at",
                table: "devices");
        }
    }
}
