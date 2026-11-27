using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderWeeklyBreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BreakEndMinute",
                table: "ProviderWeeklyHours",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BreakStartMinute",
                table: "ProviderWeeklyHours",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProviderWeeklyHours_Break",
                table: "ProviderWeeklyHours",
                sql: "([BreakStartMinute] IS NULL AND [BreakEndMinute] IS NULL)\r\nOR ([BreakStartMinute] IS NOT NULL AND [BreakEndMinute] IS NOT NULL\r\n    AND [StartMinute] IS NOT NULL AND [EndMinute] IS NOT NULL\r\n    AND [StartMinute] < [BreakStartMinute]\r\n    AND [BreakStartMinute] < [BreakEndMinute]\r\n    AND [BreakEndMinute] < [EndMinute]\r\n    AND [BreakStartMinute] % 30 = 0\r\n    AND [BreakEndMinute] % 30 = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProviderWeeklyHours_Break",
                table: "ProviderWeeklyHours");

            migrationBuilder.DropColumn(
                name: "BreakEndMinute",
                table: "ProviderWeeklyHours");

            migrationBuilder.DropColumn(
                name: "BreakStartMinute",
                table: "ProviderWeeklyHours");
        }
    }
}
