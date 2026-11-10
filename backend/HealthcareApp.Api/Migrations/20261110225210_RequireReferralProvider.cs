using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class RequireReferralProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.Referrals WHERE AssignedProviderId IS NULL)
                    THROW 51001, 'Assign a provider to every referral before applying this migration.', 1;
                """);

            DropEligibilityForeignKey(migrationBuilder);
            migrationBuilder.AlterColumn<int>(
                name: "AssignedProviderId",
                table: "Referrals",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
            AddEligibilityForeignKey(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropEligibilityForeignKey(migrationBuilder);
            migrationBuilder.AlterColumn<int>(
                name: "AssignedProviderId",
                table: "Referrals",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
            AddEligibilityForeignKey(migrationBuilder);
        }

        private const string EligibilityForeignKey =
            "FK_Referrals_ProviderServices_AssignedProviderId_ClinicalServiceId";

        private static void DropEligibilityForeignKey(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropForeignKey(name: EligibilityForeignKey, table: "Referrals");

        private static void AddEligibilityForeignKey(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddForeignKey(
                name: EligibilityForeignKey,
                table: "Referrals",
                columns: new[] { "AssignedProviderId", "ClinicalServiceId" },
                principalTable: "ProviderServices",
                principalColumns: new[] { "ProviderId", "ClinicalServiceId" },
                onDelete: ReferentialAction.Restrict);
    }
}
