using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OneLoginPerEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_ApiUser_Employee",
                table: "ApiUsers",
                column: "EmployeeId",
                unique: true,
                filter: "[EmployeeId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ApiUser_Employee",
                table: "ApiUsers");
        }
    }
}
