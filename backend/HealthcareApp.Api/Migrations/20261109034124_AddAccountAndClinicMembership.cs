using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountAndClinicMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Issuer = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    PatientId = table.Column<int>(type: "int", nullable: true),
                    IsAdministrator = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAccounts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppAccounts_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicMemberships",
                columns: table => new
                {
                    AppAccountId = table.Column<int>(type: "int", nullable: false),
                    ClinicId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicMemberships", x => new { x.AppAccountId, x.ClinicId, x.Role });
                    table.CheckConstraint("CK_ClinicMemberships_Role", "[Role] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ClinicMemberships_AppAccounts_AppAccountId",
                        column: x => x.AppAccountId,
                        principalTable: "AppAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicMemberships_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAccounts_Issuer_Subject",
                table: "AppAccounts",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppAccounts_PatientId",
                table: "AppAccounts",
                column: "PatientId",
                unique: true,
                filter: "[PatientId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppAccounts_ProviderId",
                table: "AppAccounts",
                column: "ProviderId",
                unique: true,
                filter: "[ProviderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMemberships_ClinicId",
                table: "ClinicMemberships",
                column: "ClinicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicMemberships");

            migrationBuilder.DropTable(
                name: "AppAccounts");
        }
    }
}
