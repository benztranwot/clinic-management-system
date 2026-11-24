using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailConfirmationChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailConfirmationChallenges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Issuer = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CodeSalt = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConfirmationChallenges", x => x.Id);
                    table.CheckConstraint("CK_EmailConfirmationChallenges_FailedAttempts", "[FailedAttempts] BETWEEN 0 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfirmationChallenges_Issuer_Subject_RequestedAtUtc",
                table: "EmailConfirmationChallenges",
                columns: new[] { "Issuer", "Subject", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfirmationChallenges_RequestedAtUtc",
                table: "EmailConfirmationChallenges",
                column: "RequestedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailConfirmationChallenges");
        }
    }
}
