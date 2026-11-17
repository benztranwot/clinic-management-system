using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoEmailChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoEmailChallenges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000")),
                    Issuer = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoEmailChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DemoEmailChallenges_Issuer_Subject_RequestedAtUtc",
                table: "DemoEmailChallenges",
                columns: new[] { "Issuer", "Subject", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DemoEmailChallenges_RequestedAtUtc",
                table: "DemoEmailChallenges",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DemoEmailChallenges_TokenHash",
                table: "DemoEmailChallenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoEmailChallenges_WorkspaceId",
                table: "DemoEmailChallenges",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoEmailChallenges");
        }
    }
}
