using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppAccounts_Patients_PatientId",
                table: "AppAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_AppAccounts_Providers_ProviderId",
                table: "AppAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AppAccounts_BookedByAppAccountId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ClinicalServices_ClinicalServiceId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Patients_PatientId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Providers_ProviderId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Referrals_MatchingPatientService",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicalServices_Clinics_ClinicId",
                table: "ClinicalServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicMemberships_AppAccounts_AppAccountId",
                table: "ClinicMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicMemberships_Clinics_ClinicId",
                table: "ClinicMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Providers_Clinics_ClinicId",
                table: "Providers");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderServices_ClinicalServices_ClinicalServiceId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderServices_Providers_ProviderId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderTimeOff_Providers_ProviderId",
                table: "ProviderTimeOff");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderWeeklyHours_Providers_ProviderId",
                table: "ProviderWeeklyHours");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_ClinicalServices_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Patients_PatientId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_ProviderServices_AssignedProviderId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Referrals_Id_PatientId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_AssignedProviderId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_ProviderServices_ClinicalServiceId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropIndex(
                name: "IX_ProviderServices_ProviderId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Providers_Id_ClinicId",
                table: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_Providers_ClinicId",
                table: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_ClinicMemberships_ClinicId",
                table: "ClinicMemberships");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ClinicalServices_Id_ClinicId",
                table: "ClinicalServices");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_BookedByAppAccountId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicalServiceId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ReferralId_PatientId_ClinicalServiceId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_AppAccounts_PatientId",
                table: "AppAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AppAccounts_ProviderId",
                table: "AppAccounts");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Referrals",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "ProviderWeeklyHours",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "ProviderTimeOff",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "ProviderServices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Providers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Patients",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Clinics",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "ClinicMemberships",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "ClinicalServices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AppAccounts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Referrals_WorkspaceId_Id_PatientId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "WorkspaceId", "Id", "PatientId", "ClinicalServiceId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProviderServices_WorkspaceId_ProviderId_ClinicalServiceId",
                table: "ProviderServices",
                columns: new[] { "WorkspaceId", "ProviderId", "ClinicalServiceId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Providers_WorkspaceId_Id",
                table: "Providers",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Providers_WorkspaceId_Id_ClinicId",
                table: "Providers",
                columns: new[] { "WorkspaceId", "Id", "ClinicId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Patients_WorkspaceId_Id",
                table: "Patients",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Clinics_WorkspaceId_Id",
                table: "Clinics",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ClinicalServices_WorkspaceId_Id",
                table: "ClinicalServices",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ClinicalServices_WorkspaceId_Id_ClinicId",
                table: "ClinicalServices",
                columns: new[] { "WorkspaceId", "Id", "ClinicId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppAccounts_WorkspaceId_Id",
                table: "AppAccounts",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Workspaces",
                columns: new[] { "Id", "CreatedAtUtc" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000000"), new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_WorkspaceId_AssignedProviderId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "WorkspaceId", "AssignedProviderId", "ClinicalServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_WorkspaceId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "WorkspaceId", "ClinicalServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_WorkspaceId_PatientId",
                table: "Referrals",
                columns: new[] { "WorkspaceId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderWeeklyHours_WorkspaceId_ProviderId",
                table: "ProviderWeeklyHours",
                columns: new[] { "WorkspaceId", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderTimeOff_WorkspaceId_ProviderId",
                table: "ProviderTimeOff",
                columns: new[] { "WorkspaceId", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderServices_WorkspaceId_ClinicalServiceId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "WorkspaceId", "ClinicalServiceId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderServices_WorkspaceId_ProviderId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "WorkspaceId", "ProviderId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Providers_WorkspaceId_ClinicId",
                table: "Providers",
                columns: new[] { "WorkspaceId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMemberships_WorkspaceId_AppAccountId",
                table: "ClinicMemberships",
                columns: new[] { "WorkspaceId", "AppAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMemberships_WorkspaceId_ClinicId",
                table: "ClinicMemberships",
                columns: new[] { "WorkspaceId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalServices_WorkspaceId_ClinicId",
                table: "ClinicalServices",
                columns: new[] { "WorkspaceId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_WorkspaceId_BookedByAppAccountId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "BookedByAppAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_WorkspaceId_ClinicalServiceId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "ClinicalServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_WorkspaceId_PatientId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_WorkspaceId_ProviderId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_WorkspaceId_ReferralId_PatientId_ClinicalServiceId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "ReferralId", "PatientId", "ClinicalServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAccounts_WorkspaceId_PatientId",
                table: "AppAccounts",
                columns: new[] { "WorkspaceId", "PatientId" },
                unique: true,
                filter: "[PatientId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppAccounts_WorkspaceId_ProviderId",
                table: "AppAccounts",
                columns: new[] { "WorkspaceId", "ProviderId" },
                unique: true,
                filter: "[ProviderId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AppAccounts_Patients_WorkspaceId_PatientId",
                table: "AppAccounts",
                columns: new[] { "WorkspaceId", "PatientId" },
                principalTable: "Patients",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppAccounts_Providers_WorkspaceId_ProviderId",
                table: "AppAccounts",
                columns: new[] { "WorkspaceId", "ProviderId" },
                principalTable: "Providers",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppAccounts_Workspaces_WorkspaceId",
                table: "AppAccounts",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AppAccounts_WorkspaceId_BookedByAppAccountId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "BookedByAppAccountId" },
                principalTable: "AppAccounts",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ClinicalServices_WorkspaceId_ClinicalServiceId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "ClinicalServiceId" },
                principalTable: "ClinicalServices",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Patients_WorkspaceId_PatientId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "PatientId" },
                principalTable: "Patients",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Providers_WorkspaceId_ProviderId",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "ProviderId" },
                principalTable: "Providers",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Referrals_MatchingPatientService",
                table: "Appointments",
                columns: new[] { "WorkspaceId", "ReferralId", "PatientId", "ClinicalServiceId" },
                principalTable: "Referrals",
                principalColumns: new[] { "WorkspaceId", "Id", "PatientId", "ClinicalServiceId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Workspaces_WorkspaceId",
                table: "Appointments",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicalServices_Clinics_WorkspaceId_ClinicId",
                table: "ClinicalServices",
                columns: new[] { "WorkspaceId", "ClinicId" },
                principalTable: "Clinics",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicalServices_Workspaces_WorkspaceId",
                table: "ClinicalServices",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicMemberships_AppAccounts_WorkspaceId_AppAccountId",
                table: "ClinicMemberships",
                columns: new[] { "WorkspaceId", "AppAccountId" },
                principalTable: "AppAccounts",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicMemberships_Clinics_WorkspaceId_ClinicId",
                table: "ClinicMemberships",
                columns: new[] { "WorkspaceId", "ClinicId" },
                principalTable: "Clinics",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicMemberships_Workspaces_WorkspaceId",
                table: "ClinicMemberships",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Clinics_Workspaces_WorkspaceId",
                table: "Clinics",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Workspaces_WorkspaceId",
                table: "Patients",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Providers_Clinics_WorkspaceId_ClinicId",
                table: "Providers",
                columns: new[] { "WorkspaceId", "ClinicId" },
                principalTable: "Clinics",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Providers_Workspaces_WorkspaceId",
                table: "Providers",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderServices_ClinicalServices_WorkspaceId_ClinicalServiceId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "WorkspaceId", "ClinicalServiceId", "ClinicId" },
                principalTable: "ClinicalServices",
                principalColumns: new[] { "WorkspaceId", "Id", "ClinicId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderServices_Providers_WorkspaceId_ProviderId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "WorkspaceId", "ProviderId", "ClinicId" },
                principalTable: "Providers",
                principalColumns: new[] { "WorkspaceId", "Id", "ClinicId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderServices_Workspaces_WorkspaceId",
                table: "ProviderServices",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderTimeOff_Providers_WorkspaceId_ProviderId",
                table: "ProviderTimeOff",
                columns: new[] { "WorkspaceId", "ProviderId" },
                principalTable: "Providers",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderTimeOff_Workspaces_WorkspaceId",
                table: "ProviderTimeOff",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderWeeklyHours_Providers_WorkspaceId_ProviderId",
                table: "ProviderWeeklyHours",
                columns: new[] { "WorkspaceId", "ProviderId" },
                principalTable: "Providers",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderWeeklyHours_Workspaces_WorkspaceId",
                table: "ProviderWeeklyHours",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_ClinicalServices_WorkspaceId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "WorkspaceId", "ClinicalServiceId" },
                principalTable: "ClinicalServices",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Patients_WorkspaceId_PatientId",
                table: "Referrals",
                columns: new[] { "WorkspaceId", "PatientId" },
                principalTable: "Patients",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_ProviderServices_WorkspaceId_AssignedProviderId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "WorkspaceId", "AssignedProviderId", "ClinicalServiceId" },
                principalTable: "ProviderServices",
                principalColumns: new[] { "WorkspaceId", "ProviderId", "ClinicalServiceId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Workspaces_WorkspaceId",
                table: "Referrals",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id <> '00000000-0000-0000-0000-000000000000')
                    THROW 51050, 'Private workspaces exist. Resolve their data before rolling back workspace isolation.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_AppAccounts_Patients_WorkspaceId_PatientId",
                table: "AppAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_AppAccounts_Providers_WorkspaceId_ProviderId",
                table: "AppAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_AppAccounts_Workspaces_WorkspaceId",
                table: "AppAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AppAccounts_WorkspaceId_BookedByAppAccountId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ClinicalServices_WorkspaceId_ClinicalServiceId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Patients_WorkspaceId_PatientId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Providers_WorkspaceId_ProviderId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Referrals_MatchingPatientService",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Workspaces_WorkspaceId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicalServices_Clinics_WorkspaceId_ClinicId",
                table: "ClinicalServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicalServices_Workspaces_WorkspaceId",
                table: "ClinicalServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicMemberships_AppAccounts_WorkspaceId_AppAccountId",
                table: "ClinicMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicMemberships_Clinics_WorkspaceId_ClinicId",
                table: "ClinicMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicMemberships_Workspaces_WorkspaceId",
                table: "ClinicMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Clinics_Workspaces_WorkspaceId",
                table: "Clinics");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Workspaces_WorkspaceId",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_Providers_Clinics_WorkspaceId_ClinicId",
                table: "Providers");

            migrationBuilder.DropForeignKey(
                name: "FK_Providers_Workspaces_WorkspaceId",
                table: "Providers");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderServices_ClinicalServices_WorkspaceId_ClinicalServiceId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderServices_Providers_WorkspaceId_ProviderId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderServices_Workspaces_WorkspaceId",
                table: "ProviderServices");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderTimeOff_Providers_WorkspaceId_ProviderId",
                table: "ProviderTimeOff");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderTimeOff_Workspaces_WorkspaceId",
                table: "ProviderTimeOff");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderWeeklyHours_Providers_WorkspaceId_ProviderId",
                table: "ProviderWeeklyHours");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderWeeklyHours_Workspaces_WorkspaceId",
                table: "ProviderWeeklyHours");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_ClinicalServices_WorkspaceId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Patients_WorkspaceId_PatientId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_ProviderServices_WorkspaceId_AssignedProviderId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Workspaces_WorkspaceId",
                table: "Referrals");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Referrals_WorkspaceId_Id_PatientId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_WorkspaceId_AssignedProviderId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_WorkspaceId_ClinicalServiceId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_WorkspaceId_PatientId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_ProviderWeeklyHours_WorkspaceId_ProviderId",
                table: "ProviderWeeklyHours");

            migrationBuilder.DropIndex(
                name: "IX_ProviderTimeOff_WorkspaceId_ProviderId",
                table: "ProviderTimeOff");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProviderServices_WorkspaceId_ProviderId_ClinicalServiceId",
                table: "ProviderServices");

            migrationBuilder.DropIndex(
                name: "IX_ProviderServices_WorkspaceId_ClinicalServiceId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropIndex(
                name: "IX_ProviderServices_WorkspaceId_ProviderId_ClinicId",
                table: "ProviderServices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Providers_WorkspaceId_Id",
                table: "Providers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Providers_WorkspaceId_Id_ClinicId",
                table: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_Providers_WorkspaceId_ClinicId",
                table: "Providers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Patients_WorkspaceId_Id",
                table: "Patients");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Clinics_WorkspaceId_Id",
                table: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_ClinicMemberships_WorkspaceId_AppAccountId",
                table: "ClinicMemberships");

            migrationBuilder.DropIndex(
                name: "IX_ClinicMemberships_WorkspaceId_ClinicId",
                table: "ClinicMemberships");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ClinicalServices_WorkspaceId_Id",
                table: "ClinicalServices");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ClinicalServices_WorkspaceId_Id_ClinicId",
                table: "ClinicalServices");

            migrationBuilder.DropIndex(
                name: "IX_ClinicalServices_WorkspaceId_ClinicId",
                table: "ClinicalServices");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_WorkspaceId_BookedByAppAccountId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_WorkspaceId_ClinicalServiceId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_WorkspaceId_PatientId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_WorkspaceId_ProviderId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_WorkspaceId_ReferralId_PatientId_ClinicalServiceId",
                table: "Appointments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppAccounts_WorkspaceId_Id",
                table: "AppAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AppAccounts_WorkspaceId_PatientId",
                table: "AppAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AppAccounts_WorkspaceId_ProviderId",
                table: "AppAccounts");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ProviderWeeklyHours");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ProviderTimeOff");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ProviderServices");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ClinicMemberships");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ClinicalServices");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AppAccounts");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Referrals_Id_PatientId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "Id", "PatientId", "ClinicalServiceId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Providers_Id_ClinicId",
                table: "Providers",
                columns: new[] { "Id", "ClinicId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ClinicalServices_Id_ClinicId",
                table: "ClinicalServices",
                columns: new[] { "Id", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_AssignedProviderId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "AssignedProviderId", "ClinicalServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ClinicalServiceId",
                table: "Referrals",
                column: "ClinicalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderServices_ClinicalServiceId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "ClinicalServiceId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderServices_ProviderId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "ProviderId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Providers_ClinicId",
                table: "Providers",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMemberships_ClinicId",
                table: "ClinicMemberships",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BookedByAppAccountId",
                table: "Appointments",
                column: "BookedByAppAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicalServiceId",
                table: "Appointments",
                column: "ClinicalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ReferralId_PatientId_ClinicalServiceId",
                table: "Appointments",
                columns: new[] { "ReferralId", "PatientId", "ClinicalServiceId" });

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

            migrationBuilder.AddForeignKey(
                name: "FK_AppAccounts_Patients_PatientId",
                table: "AppAccounts",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppAccounts_Providers_ProviderId",
                table: "AppAccounts",
                column: "ProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AppAccounts_BookedByAppAccountId",
                table: "Appointments",
                column: "BookedByAppAccountId",
                principalTable: "AppAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ClinicalServices_ClinicalServiceId",
                table: "Appointments",
                column: "ClinicalServiceId",
                principalTable: "ClinicalServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Patients_PatientId",
                table: "Appointments",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Providers_ProviderId",
                table: "Appointments",
                column: "ProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Referrals_MatchingPatientService",
                table: "Appointments",
                columns: new[] { "ReferralId", "PatientId", "ClinicalServiceId" },
                principalTable: "Referrals",
                principalColumns: new[] { "Id", "PatientId", "ClinicalServiceId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicalServices_Clinics_ClinicId",
                table: "ClinicalServices",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicMemberships_AppAccounts_AppAccountId",
                table: "ClinicMemberships",
                column: "AppAccountId",
                principalTable: "AppAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicMemberships_Clinics_ClinicId",
                table: "ClinicMemberships",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Providers_Clinics_ClinicId",
                table: "Providers",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderServices_ClinicalServices_ClinicalServiceId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "ClinicalServiceId", "ClinicId" },
                principalTable: "ClinicalServices",
                principalColumns: new[] { "Id", "ClinicId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderServices_Providers_ProviderId_ClinicId",
                table: "ProviderServices",
                columns: new[] { "ProviderId", "ClinicId" },
                principalTable: "Providers",
                principalColumns: new[] { "Id", "ClinicId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderTimeOff_Providers_ProviderId",
                table: "ProviderTimeOff",
                column: "ProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderWeeklyHours_Providers_ProviderId",
                table: "ProviderWeeklyHours",
                column: "ProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_ClinicalServices_ClinicalServiceId",
                table: "Referrals",
                column: "ClinicalServiceId",
                principalTable: "ClinicalServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Patients_PatientId",
                table: "Referrals",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_ProviderServices_AssignedProviderId_ClinicalServiceId",
                table: "Referrals",
                columns: new[] { "AssignedProviderId", "ClinicalServiceId" },
                principalTable: "ProviderServices",
                principalColumns: new[] { "ProviderId", "ClinicalServiceId" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
