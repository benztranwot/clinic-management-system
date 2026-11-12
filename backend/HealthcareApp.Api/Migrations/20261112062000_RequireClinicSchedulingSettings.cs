using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class RequireClinicSchedulingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fill missing settings for the known demo clinic before enforcing required columns.
            migrationBuilder.Sql("""
                UPDATE dbo.Clinics
                SET Address = COALESCE(NULLIF(TRIM(Address), N''), N'100 Demo Avenue'),
                    TimeZoneId = COALESCE(NULLIF(TRIM(TimeZoneId), N''), N'America/Chicago')
                WHERE PublicId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
                    AND (NULLIF(TRIM(Address), N'') IS NULL OR NULLIF(TRIM(TimeZoneId), N'') IS NULL);

                IF EXISTS (
                    SELECT 1 FROM dbo.Clinics
                    WHERE NULLIF(TRIM(Address), N'') IS NULL OR NULLIF(TRIM(TimeZoneId), N'') IS NULL
                )
                    THROW 51002, 'Fill in the address and IANA time zone for each existing clinic before retrying this migration.', 1;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "Clinics",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "Clinics",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);
        }
    }
}
