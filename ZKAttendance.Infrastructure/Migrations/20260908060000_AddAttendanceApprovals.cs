using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKAttendance.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the day-level approval queue for late arrivals.
    ///
    /// The office-hours rules themselves need no migration: they live in the
    /// existing SystemSettings table under the "Attendance" category, so they
    /// can be changed from the Settings screen without a deployment.
    /// </summary>
    public partial class AddAttendanceApprovals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceApprovals",
                columns: table => new
                {
                    ApprovalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FirstCheckIn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MinutesLate = table.Column<int>(type: "int", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DecidedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceApprovals", x => x.ApprovalId);
                    table.ForeignKey(
                        name: "FK_AttendanceApprovals_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_Employee_Date",
                table: "AttendanceApprovals",
                columns: new[] { "EmployeeId", "AttendanceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_Status_Date",
                table: "AttendanceApprovals",
                columns: new[] { "Status", "AttendanceDate" });

            // Seed the default rules so the Settings screen opens populated
            // rather than blank on first run: 10:00 start, 15 minutes grace,
            // approval needed after 11:00.
            migrationBuilder.Sql(@"
                MERGE INTO SystemSettings AS target
                USING (VALUES
                    ('OfficeStartTime',              '10:00', 'Working day start time'),
                    ('OfficeEndTime',                '18:00', 'Working day end time'),
                    ('GraceMinutes',                 '15',    'Minutes after the start that still count as on time'),
                    ('ApprovalRequiredAfterMinutes', '60',    'Minutes after the start beyond which an admin must approve'),
                    ('RequireApprovalForLate',       'false', 'Also require approval between grace and the cut-off'),
                    ('CloseGraceMinutes',            '30',    'Minutes after the end time before a no-show is marked absent'),
                    ('HalfDayUnderHours',            '4',     'Hours below which a present day counts as a half day')
                ) AS source (SettingKey, SettingValue, Description)
                ON target.SettingKey = source.SettingKey AND target.Category = 'Attendance'
                WHEN NOT MATCHED THEN
                    INSERT (SettingKey, SettingValue, Category, Description, IsActive, CreatedDate)
                    VALUES (source.SettingKey, source.SettingValue, 'Attendance', source.Description, 1, GETDATE());
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AttendanceApprovals");

            migrationBuilder.Sql(
                "DELETE FROM SystemSettings WHERE Category = 'Attendance';");
        }
    }
}
