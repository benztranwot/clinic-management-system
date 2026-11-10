using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderTimeOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderTimeOff",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderTimeOff", x => x.Id);
                    table.CheckConstraint("CK_ProviderTimeOff_Interval", "[StartUtc] < [EndUtc] AND DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_ProviderTimeOff_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderTimeOff_ProviderId_StartUtc",
                table: "ProviderTimeOff",
                columns: new[] { "ProviderId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderTimeOff_PublicId",
                table: "ProviderTimeOff",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderTimeOff");
        }
    }
}
