using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "FirmwareVersion",
                table: "DeviceStatuses",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeviceId1",
                table: "AttendanceLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_DeviceId1",
                table: "AttendanceLogs",
                column: "DeviceId1");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceLogs_Devices_DeviceId1",
                table: "AttendanceLogs",
                column: "DeviceId1",
                principalTable: "Devices",
                principalColumn: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceLogs_Devices_DeviceId1",
                table: "AttendanceLogs");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceLogs_DeviceId1",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "DeviceId1",
                table: "AttendanceLogs");

            migrationBuilder.AlterColumn<string>(
                name: "FirmwareVersion",
                table: "DeviceStatuses",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
