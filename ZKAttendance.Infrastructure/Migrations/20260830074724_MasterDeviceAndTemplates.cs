using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MasterDeviceAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employee_BiometricUserId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDevices_DeviceId",
                table: "EmployeeDevices");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeeDevices_EmployeeId_DeviceId",
                table: "EmployeeDevices",
                newName: "IX_EmployeeDevice_Employee_Device");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "EmployeeDevices",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedDate",
                table: "EmployeeDevices",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<string>(
                name: "BiometricUserId",
                table: "EmployeeDevices",
                type: "varchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "EnrolledDate",
                table: "EmployeeDevices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnrolled",
                table: "EmployeeDevices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisioned",
                table: "Devices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEnrollmentPullDate",
                table: "Devices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Devices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FingerprintTemplates",
                columns: table => new
                {
                    TemplateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    FingerIndex = table.Column<int>(type: "int", nullable: false),
                    TemplateData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TemplateFormatVersion = table.Column<int>(type: "int", nullable: false),
                    SourceDeviceId = table.Column<int>(type: "int", nullable: false),
                    CapturedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FingerprintTemplates", x => x.TemplateId);
                    table.ForeignKey(
                        name: "FK_FingerprintTemplates_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingEnrollments",
                columns: table => new
                {
                    PendingEnrollmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    DeviceUserId = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    NameOnDevice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FingerCount = table.Column<int>(type: "int", nullable: false),
                    Privilege = table.Column<int>(type: "int", nullable: false),
                    DiscoveredDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingEnrollments", x => x.PendingEnrollmentId);
                    table.ForeignKey(
                        name: "FK_PendingEnrollments_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employee_BiometricUserId",
                table: "Employees",
                column: "BiometricUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDevice_Device_BiometricId",
                table: "EmployeeDevices",
                columns: new[] { "DeviceId", "BiometricUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Device_SingleMaster",
                table: "Devices",
                column: "Role",
                unique: true,
                filter: "[Role] = 1 AND [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Template_Employee_Finger",
                table: "FingerprintTemplates",
                columns: new[] { "EmployeeId", "FingerIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Pending_Device_UserId",
                table: "PendingEnrollments",
                columns: new[] { "DeviceId", "DeviceUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FingerprintTemplates");

            migrationBuilder.DropTable(
                name: "PendingEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_Employee_BiometricUserId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDevice_Device_BiometricId",
                table: "EmployeeDevices");

            migrationBuilder.DropIndex(
                name: "UX_Device_SingleMaster",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "BiometricUserId",
                table: "EmployeeDevices");

            migrationBuilder.DropColumn(
                name: "EnrolledDate",
                table: "EmployeeDevices");

            migrationBuilder.DropColumn(
                name: "IsEnrolled",
                table: "EmployeeDevices");

            migrationBuilder.DropColumn(
                name: "IsProvisioned",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastEnrollmentPullDate",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Devices");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeeDevice_Employee_Device",
                table: "EmployeeDevices",
                newName: "IX_EmployeeDevices_EmployeeId_DeviceId");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "EmployeeDevices",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedDate",
                table: "EmployeeDevices",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETDATE()");

            migrationBuilder.CreateIndex(
                name: "IX_Employee_BiometricUserId",
                table: "Employees",
                column: "BiometricUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDevices_DeviceId",
                table: "EmployeeDevices",
                column: "DeviceId");
        }
    }
}
