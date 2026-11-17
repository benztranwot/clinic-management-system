using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class DemoWorkspaceTests
{
    [Fact]
    public void ModelScopesEachRecordAndRelationship()
    {
        var options = new DbContextOptionsBuilder<HealthcareDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=WorkspaceModel;Integrated Security=True")
            .Options;
        using var db = new HealthcareDbContext(options);

        foreach (var entity in db.Model.GetEntityTypes())
        {
            Assert.True(typeof(IWorkspaceRecord).IsAssignableFrom(entity.ClrType), entity.DisplayName());
            Assert.NotEmpty(entity.GetDeclaredQueryFilters());
            Assert.True(entity.FindProperty(nameof(IWorkspaceRecord.WorkspaceId))?.IsConcurrencyToken,
                entity.DisplayName());

            foreach (var relationship in entity.GetForeignKeys())
            {
                Assert.Equal(nameof(IWorkspaceRecord.WorkspaceId), relationship.Properties[0].Name);
                Assert.Equal(nameof(IWorkspaceRecord.WorkspaceId), relationship.PrincipalKey.Properties[0].Name);
            }
        }

        Assert.Contains("WHERE [c].[WorkspaceId] =", db.Clinics.ToQueryString());
    }

    [Fact]
    public async Task AdministratorAccessIsBoundToTheAuthenticatedWorkspace()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var authenticated = tokens.CreateFactory(factory);
        await using var configured = authenticated.WithWebHostBuilder(webHost =>
            webHost.UseSetting("DemoWorkspaces:Enabled", "true"));

        using var first = configured.CreateClient();
        using var second = configured.CreateClient();
        using var shared = configured.CreateClient();
        tokens.AuthorizeSubject(first, "auth0|first-demo", "read:clinics", "manage:clinics", "manage:patients");
        tokens.AuthorizeSubject(second, "auth0|second-demo", "read:clinics");
        tokens.AuthorizeSubject(shared, "auth0|existing-staff", "read:clinics");

        await using var scope = configured.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        var firstWorkspace = Guid.NewGuid();
        var secondWorkspace = Guid.NewGuid();
        var firstClinic = Guid.NewGuid();
        var secondClinic = Guid.NewGuid();
        var sharedClinic = Guid.NewGuid();

        try
        {
            // Migrate real pre-workspace records so the upgrade exercises the defaults and new foreign keys.
            await db.GetService<IMigrator>().MigrateAsync("20261112062000_RequireClinicSchedulingSettings");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.AppAccounts (Issuer, Subject, IsAdministrator) VALUES
                    ({TestJwtIssuer.Issuer}, N'auth0|existing-staff', 0);
                INSERT INTO dbo.Clinics (PublicId, Name, Address, TimeZoneId) VALUES
                    ({sharedClinic}, N'Shared Clinic', N'100 Demo Avenue', N'America/Chicago');
                INSERT INTO dbo.ClinicMemberships (AppAccountId, ClinicId, Role)
                SELECT account.Id, clinic.Id, {(int)ClinicStaffRole.FrontDesk}
                FROM dbo.AppAccounts account CROSS JOIN dbo.Clinics clinic
                WHERE account.Subject = N'auth0|existing-staff' AND clinic.PublicId = {sharedClinic};
                """);

            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.AppAccounts (Issuer, Subject, IsAdministrator, WorkspaceId) VALUES
                    ({TestJwtIssuer.Issuer}, N'auth0|first-demo', 1, {firstWorkspace}),
                    ({TestJwtIssuer.Issuer}, N'auth0|second-demo', 1, {secondWorkspace});
                INSERT INTO dbo.Clinics (PublicId, Name, Address, TimeZoneId, WorkspaceId) VALUES
                    ({firstClinic}, N'First Clinic', N'100 Demo Avenue', N'America/Chicago', {firstWorkspace}),
                    ({secondClinic}, N'Second Clinic', N'100 Demo Avenue', N'America/Chicago', {secondWorkspace});
                """);

            await AssertOnlyClinicAsync(first, firstClinic);
            await AssertOnlyClinicAsync(second, secondClinic);
            await AssertOnlyClinicAsync(shared, sharedClinic);

            using var staffProfile = await shared.GetAsync("/api/me");
            staffProfile.EnsureSuccessStatusCode();
            var staffAccount = await staffProfile.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(staffAccount.GetProperty("isAdministrator").GetBoolean());
            Assert.Equal("FrontDesk", Assert.Single(staffAccount.GetProperty("memberships").EnumerateArray())
                .GetProperty("role").GetString());

            using var delete = await first.DeleteAsync($"/api/clinics/{secondClinic}");
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
            await AssertOnlyClinicAsync(second, secondClinic);

            using var create = await first.PostAsJsonAsync("/api/patients", new
            {
                firstName = "Alex",
                lastName = "Demo",
                dateOfBirth = new DateOnly(2000, 1, 1),
                workspaceId = secondWorkspace,
            });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var savedPatients = await db.Patients.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            Assert.Equal(firstWorkspace, Assert.Single(savedPatients).WorkspaceId);

            // A foreign key cannot link a row to an entity in another workspace.
            var crossWorkspaceLink = await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.Providers (PublicId, FirstName, LastName, ClinicId, WorkspaceId)
                    SELECT {Guid.NewGuid()}, N'Cross', N'Workspace', Id, {firstWorkspace}
                    FROM dbo.Clinics WHERE PublicId = {secondClinic};
                    """));
            Assert.Equal(547, crossWorkspaceLink.Number);

            var downgrade = await Assert.ThrowsAsync<SqlException>(() =>
                db.GetService<IMigrator>().MigrateAsync("20261112062000_RequireClinicSchedulingSettings"));
            Assert.Equal(51001, downgrade.Number);
            await AssertOnlyClinicAsync(first, firstClinic);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertOnlyClinicAsync(HttpClient client, Guid expectedClinic)
    {
        using var response = await client.GetAsync("/api/clinics");
        response.EnsureSuccessStatusCode();
        var clinics = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedClinic, Assert.Single(clinics.EnumerateArray()).GetProperty("publicId").GetGuid());
    }
}
