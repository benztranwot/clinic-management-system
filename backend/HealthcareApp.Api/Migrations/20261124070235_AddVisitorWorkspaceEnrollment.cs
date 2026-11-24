using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitorWorkspaceEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A narrow ownership-chained operation preserves read-only account-table access.
            migrationBuilder.Sql("""
                EXEC(N'CREATE PROCEDURE dbo.CreateVisitorWorkspaceAccount
                    @WorkspaceId uniqueidentifier,
                    @Issuer nvarchar(255),
                    @Subject nvarchar(255),
                    @ProviderId int,
                    @AccountId int OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;
                    IF @@TRANCOUNT = 0
                        THROW 51060, ''Visitor enrollment requires a transaction.'', 1;
                    IF @WorkspaceId IS NULL OR @WorkspaceId = ''00000000-0000-0000-0000-000000000000''
                        THROW 51060, ''Visitor enrollment requires a private workspace.'', 1;
                    IF NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = @WorkspaceId)
                        THROW 51060, ''The private workspace does not exist.'', 1;
                    IF EXISTS (SELECT 1 FROM dbo.AppAccounts WITH (UPDLOCK, HOLDLOCK) WHERE WorkspaceId = @WorkspaceId)
                        THROW 51060, ''This workspace already has an owner.'', 1;
                    IF NOT EXISTS (SELECT 1 FROM dbo.Providers WHERE WorkspaceId = @WorkspaceId AND Id = @ProviderId)
                        THROW 51060, ''The selected provider does not belong to this workspace.'', 1;

                    INSERT INTO dbo.AppAccounts (WorkspaceId, Issuer, Subject, ProviderId, IsAdministrator)
                        VALUES (@WorkspaceId, @Issuer, @Subject, @ProviderId, 1);
                    SET @AccountId = CONVERT(int, SCOPE_IDENTITY());
                    INSERT INTO dbo.ClinicMemberships (WorkspaceId, AppAccountId, ClinicId, Role)
                        SELECT @WorkspaceId, @AccountId, ClinicId, 1 FROM dbo.Providers
                        WHERE WorkspaceId = @WorkspaceId AND Id = @ProviderId;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE dbo.CreateVisitorWorkspaceAccount;");
        }
    }
}
