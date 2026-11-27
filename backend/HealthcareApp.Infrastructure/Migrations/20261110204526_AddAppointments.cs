using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Referrals_Id_PatientId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "Id", "PatientId", "ClinicalServiceId" });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    ClinicalServiceId = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    ReferralId = table.Column<int>(type: "int", nullable: true),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BookedByAppAccountId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_Status", "[Status] IN (1, 2, 3)");
                    table.CheckConstraint("CK_Appointments_UtcSlot", "DATEPART(TZOFFSET, [StartUtc]) = 0\r\nAND DATEPART(MINUTE, [StartUtc]) % 30 = 0\r\nAND DATEPART(SECOND, [StartUtc]) = 0\r\nAND DATEPART(NANOSECOND, [StartUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_Appointments_AppAccounts_BookedByAppAccountId",
                        column: x => x.BookedByAppAccountId,
                        principalTable: "AppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_ClinicalServices_ClinicalServiceId",
                        column: x => x.ClinicalServiceId,
                        principalTable: "ClinicalServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Referrals_MatchingPatientService",
                        columns: x => new { x.ReferralId, x.PatientId, x.ClinicalServiceId },
                        principalTable: "Referrals",
                        principalColumns: new[] { "Id", "PatientId", "ClinicalServiceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BookedByAppAccountId",
                table: "Appointments",
                column: "BookedByAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicalServiceId",
                table: "Appointments",
                column: "ClinicalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PublicId",
                table: "Appointments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ReferralId_PatientId_ClinicalServiceId",
                table: "Appointments",
                columns: new[] { "ReferralId", "PatientId", "ClinicalServiceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_ConfirmedPatientSlot",
                table: "Appointments",
                columns: new[] { "PatientId", "StartUtc" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_ConfirmedProviderSlot",
                table: "Appointments",
                columns: new[] { "ProviderId", "StartUtc" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_NonCancelledReferral",
                table: "Appointments",
                column: "ReferralId",
                unique: true,
                filter: "[ReferralId] IS NOT NULL AND [Status] <> 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Referrals_Id_PatientId_ClinicalServiceId",
                table: "Referrals");
        }
    }
}
