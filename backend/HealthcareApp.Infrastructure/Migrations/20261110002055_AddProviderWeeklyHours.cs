using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderWeeklyHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderWeeklyHours",
                columns: table => new
                {
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartMinute = table.Column<int>(type: "int", nullable: true),
                    EndMinute = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderWeeklyHours", x => new { x.ProviderId, x.DayOfWeek });
                    table.CheckConstraint("CK_ProviderWeeklyHours_Day", "[DayOfWeek] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_ProviderWeeklyHours_Window", "([StartMinute] IS NULL AND [EndMinute] IS NULL)\r\nOR ([StartMinute] IS NOT NULL AND [EndMinute] IS NOT NULL\r\n    AND [StartMinute] >= 0 AND [EndMinute] <= 1440\r\n    AND [StartMinute] < [EndMinute]\r\n    AND [StartMinute] % 30 = 0 AND [EndMinute] % 30 = 0)");
                    table.ForeignKey(
                        name: "FK_ProviderWeeklyHours_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderWeeklyHours");
        }
    }
}
