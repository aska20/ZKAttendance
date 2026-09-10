using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueEmployeeEmailIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Employee_Email",
                table: "Employees",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL AND [Email] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Employee_Email",
                table: "Employees");
        }
    }
}
