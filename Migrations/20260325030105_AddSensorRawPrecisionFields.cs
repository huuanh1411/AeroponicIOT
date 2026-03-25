using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AeroponicIOT.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorRawPrecisionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "humidity_raw",
                table: "sensor_logs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "light_intensity_raw",
                table: "sensor_logs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tds_raw",
                table: "sensor_logs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "water_temp_raw",
                table: "sensor_logs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "humidity_raw",
                table: "sensor_logs");

            migrationBuilder.DropColumn(
                name: "light_intensity_raw",
                table: "sensor_logs");

            migrationBuilder.DropColumn(
                name: "tds_raw",
                table: "sensor_logs");

            migrationBuilder.DropColumn(
                name: "water_temp_raw",
                table: "sensor_logs");
        }
    }
}
