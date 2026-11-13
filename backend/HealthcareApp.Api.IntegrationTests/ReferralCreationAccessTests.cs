using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ReferralCreationAccessTests
{
    [Fact]
    public async Task CreateReferral_UsesExactReceivingPairAndVerifiedClinician()
    {
        const string clinicianSubject = "auth0|referral-clinician";
        const string frontDeskSubject = "auth0|referral-front-desk";
        string[] permissions =
        [
            "manage:referrals", "read:clinics", "read:services", "read:providers", "read:provider-services", "read:patients",
        ];
        const string optionsUrl = "/api/referrals/receiving-options";

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();
            var north = new Clinic { Name = "North Clinic", Address = "100 Test Avenue", TimeZoneId = "America/Chicago" };
            var south = new Clinic { Name = "South Clinic", Address = "200 Test Avenue", TimeZoneId = "America/Chicago" };
            var patient = new Patient { FirstName = "Taylor", LastName = "Demo", DateOfBirth = new DateOnly(1998, 4, 12) };
            db.AddRange(north, south, patient);
            await db.SaveChangesAsync();

            var northService = new ClinicalService { ClinicId = north.Id, Name = "Physical exam" };
            var southService = new ClinicalService { ClinicId = south.Id, Name = northService.Name };
            db.AddRange(northService, southService);
            await db.SaveChangesAsync();
            var referringProvider = await ReferralTestData.AddEligibleProviderAsync(db, northService, "Beth");
            var receivingProvider = await ReferralTestData.AddEligibleProviderAsync(db, southService, "Sam");
            db.Providers.Add(new Provider { ClinicId = south.Id, FirstName = "Casey", LastName = "Unlinked" });

            var clinician = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = clinicianSubject, ProviderId = referringProvider.Id };
            var frontDesk = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = frontDeskSubject };
            db.AppAccounts.AddRange(clinician, frontDesk);
            await db.SaveChangesAsync();
            db.ClinicMemberships.AddRange(
                new ClinicMembership { AppAccountId = clinician.Id, ClinicId = north.Id, Role = ClinicStaffRole.Clinician },
                new ClinicMembership { AppAccountId = frontDesk.Id, ClinicId = north.Id, Role = ClinicStaffRole.FrontDesk });
            await db.SaveChangesAsync();

            var request = new
            {
                PatientPublicId = patient.PublicId,
                ClinicalServicePublicId = southService.PublicId,
                AssignedProviderPublicId = receivingProvider.PublicId,
                ReferringProviderName = "Caller-supplied name",
                Reason = "Routine examination",
            };

            tokens.AuthorizeSubject(client, frontDeskSubject, permissions);
            var frontDeskAccount = await client.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.False(frontDeskAccount.GetProperty("canCreateReferrals").GetBoolean());
            using var deniedOptions = await client.GetAsync(optionsUrl);
            Assert.Equal(HttpStatusCode.Forbidden, deniedOptions.StatusCode);
            using var deniedCreate = await client.PostAsJsonAsync("/api/referrals", request);
            Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);

            foreach (var permission in permissions)
            {
                tokens.AuthorizeSubject(client, clinicianSubject, permissions.Where(value => value != permission).ToArray());
                var accountWithoutPermission = await client.GetFromJsonAsync<JsonElement>("/api/me");
                Assert.False(accountWithoutPermission.GetProperty("canCreateReferrals").GetBoolean());

                // The patient picker has its own permission. The receiving-options route does not return patient data.
                using var optionsWithoutPermission = await client.GetAsync(optionsUrl);
                Assert.Equal(permission == "read:patients" ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                    optionsWithoutPermission.StatusCode);
            }

            tokens.AuthorizeSubject(client, clinicianSubject, permissions);
            var clinicianAccount = await client.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.True(clinicianAccount.GetProperty("canCreateReferrals").GetBoolean());
            using var options = await client.GetAsync(optionsUrl);
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            var body = await options.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Clinician Beth Demo", body.GetProperty("referringProviderName").GetString());
            Assert.Collection(body.GetProperty("destinations").EnumerateArray(),
                item =>
                {
                    Assert.Equal(north.PublicId, item.GetProperty("clinicPublicId").GetGuid());
                    Assert.Equal(northService.PublicId, item.GetProperty("clinicalServicePublicId").GetGuid());
                    Assert.Equal(referringProvider.PublicId, item.GetProperty("providerPublicId").GetGuid());
                },
                item =>
                {
                    Assert.Equal(south.PublicId, item.GetProperty("clinicPublicId").GetGuid());
                    Assert.Equal(southService.PublicId, item.GetProperty("clinicalServicePublicId").GetGuid());
                    Assert.Equal(receivingProvider.PublicId, item.GetProperty("providerPublicId").GetGuid());
                });

            using var mismatch = await client.PostAsJsonAsync("/api/referrals",
                request with { AssignedProviderPublicId = referringProvider.PublicId });
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

            using var created = await client.PostAsJsonAsync("/api/referrals", request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var saved = await db.Referrals.AsNoTracking().SingleAsync();
            Assert.Equal("Clinician Beth Demo", saved.ReferringProviderName);
            Assert.Equal(southService.Id, saved.ClinicalServiceId);
            Assert.Equal(receivingProvider.Id, saved.AssignedProviderId);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
