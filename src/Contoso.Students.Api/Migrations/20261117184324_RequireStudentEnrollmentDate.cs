using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contoso.Students.Api.Migrations
{
    /// <inheritdoc />
    public partial class RequireStudentEnrollmentDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Students_EnrollmentDate_NotDefault",
                table: "Students",
                sql: "[EnrollmentDate] > '0001-01-01T00:00:00'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Destructive database rollback is not supported.");
        }
    }
}
