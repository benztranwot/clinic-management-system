using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clinics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalServices", x => x.Id);
                    table.UniqueConstraint("AK_ClinicalServices_Id_ClinicId", x => new { x.Id, x.ClinicId });
                    table.ForeignKey(
                        name: "FK_ClinicalServices_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                    table.UniqueConstraint("AK_Providers_Id_ClinicId", x => new { x.Id, x.ClinicId });
                    table.ForeignKey(
                        name: "FK_Providers_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderServices",
                columns: table => new
                {
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    ClinicalServiceId = table.Column<int>(type: "int", nullable: false),
                    ClinicId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderServices", x => new { x.ProviderId, x.ClinicalServiceId });
                    table.ForeignKey(
                        name: "FK_ProviderServices_ClinicalServices_ClinicalServiceId_ClinicId",
                        columns: x => new { x.ClinicalServiceId, x.ClinicId },
                        principalTable: "ClinicalServices",
                        principalColumns: new[] { "Id", "ClinicId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderServices_Providers_ProviderId_ClinicId",
                        columns: x => new { x.ProviderId, x.ClinicId },
                        principalTable: "Providers",
                        principalColumns: new[] { "Id", "ClinicId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    ClinicalServiceId = table.Column<int>(type: "int", nullable: false),
                    AssignedProviderId = table.Column<int>(type: "int", nullable: true),
                    ReferringProviderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MissingInformationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.CheckConstraint("CK_Referrals_Status", "[Status] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_Referrals_ClinicalServices_ClinicalServiceId",
                        column: x => x.ClinicalServiceId,
                        principalTable: "ClinicalServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_ProviderServices_AssignedProviderId_ClinicalServiceId",
                        columns: x => new { x.AssignedProviderId, x.ClinicalServiceId },
                        principalTable: "ProviderServices",
                        principalColumns: new[] { "ProviderId", "ClinicalServiceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalServices_ClinicId_Name",
                table: "ClinicalServices",
                columns: new[] { "ClinicId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalServices_PublicId",
                table: "ClinicalServices",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_PublicId",
                table: "Clinics",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_PublicId",
                table: "Patients",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_ClinicId",
                table: "Providers",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Providers_PublicId",
                table: "Providers",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderServices_ClinicalServiceId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "ClinicalServiceId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderServices_ProviderId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "ProviderId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_AssignedProviderId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "AssignedProviderId", "ClinicalServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ClinicalServiceId",
                table: "Referrals",
                column: "ClinicalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_PatientId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "PatientId", "ClinicalServiceId" },
                unique: true,
                filter: "[Status] <> 4");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_PublicId",
                table: "Referrals",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropTable(
                name: "Patients");

            migrationBuilder.DropTable(
                name: "ProviderServices");

            migrationBuilder.DropTable(
                name: "ClinicalServices");

            migrationBuilder.DropTable(
                name: "Providers");

            migrationBuilder.DropTable(
                name: "Clinics");
        }
    }
}
