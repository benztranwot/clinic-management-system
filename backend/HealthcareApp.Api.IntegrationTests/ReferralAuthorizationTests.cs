using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ReferralAuthorizationTests
{
    [Theory]
    [InlineData("List", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("List", true, null, HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:patients", HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:referrals", HttpStatusCode.OK)]
    [InlineData("Read", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Read", true, null, HttpStatusCode.Forbidden)]
    [InlineData("Read", true, "read:referrals", HttpStatusCode.OK)]
    [InlineData("Eligible", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Eligible", true, "read:providers read:provider-services", HttpStatusCode.Forbidden)]
    [InlineData("Eligible", true, "read:referrals read:provider-services", HttpStatusCode.Forbidden)]
    [InlineData("Eligible", true, "read:referrals read:providers", HttpStatusCode.Forbidden)]
    [InlineData("Eligible", true, "read:referrals read:providers read:provider-services", HttpStatusCode.OK)]
    [InlineData("Create", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Create", true, "read:referrals", HttpStatusCode.Forbidden)]
    [InlineData("Create", true, "manage:referrals", HttpStatusCode.Created)]
    [InlineData("Assign", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Assign", true, "read:referrals", HttpStatusCode.Forbidden)]
    [InlineData("Assign", true, "manage:referrals", HttpStatusCode.OK)]
    [InlineData("Status", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Status", true, "read:referrals", HttpStatusCode.Forbidden)]
    [InlineData("Status", true, "manage:referrals", HttpStatusCode.OK)]
    [InlineData("ReadyWithoutProvider", true, "manage:referrals", HttpStatusCode.Conflict)]
    [InlineData("ReadyWithProvider", true, "manage:referrals", HttpStatusCode.OK)]
    [InlineData("ClearNote", true, "manage:referrals", HttpStatusCode.OK)]
    [InlineData("Close", true, "manage:referrals", HttpStatusCode.OK)]
    public async Task ReferralRoutes_AccessAndWorkflowRules_ReturnExpectedStatus(
        string operation,
        bool sendToken,
        string? permission,
        HttpStatusCode expectedStatus
    )
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        if (sendToken)
        {
            string[] permissions = permission?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            tokens.Authorize(client, permissions);
        }

        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the database target before creating or deleting anything.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "Referral Authorization Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1990, 5, 10),
            };
            var otherPatient = new Patient
            {
                FirstName = "Morgan",
                LastName = "Example",
                DateOfBirth = new DateOnly(1996, 6, 15),
            };
            db.Clinics.Add(clinic);
            db.Patients.AddRange(patient, otherPatient);
            await db.SaveChangesAsync();

            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Nutrition consultation" };
            var provider = new Provider
            {
                ClinicId = clinic.Id,
                FirstName = "Jordan",
                LastName = "Demo",
            };
            db.ClinicalServices.Add(service);
            db.Providers.Add(provider);
            await db.SaveChangesAsync();

            // Make this provider eligible for the requested service.
            db.ProviderServices.Add(new ProviderService
            {
                ProviderId = provider.Id,
                ClinicalServiceId = service.Id,
                ClinicId = clinic.Id,
            });
            await db.SaveChangesAsync();

            if (operation == "Eligible" && expectedStatus == HttpStatusCode.OK)
            {
                // A provider at this clinic is not eligible without the requested service link.
                db.Providers.Add(new Provider { ClinicId = clinic.Id, FirstName = "Casey", LastName = "Unlinked" });
                var otherClinic = new Clinic { Name = "Other Referral Clinic" };
                db.Clinics.Add(otherClinic);
                await db.SaveChangesAsync();

                // A service with the same name at a different clinic is a distinct service.
                var sameNameService = new ClinicalService { ClinicId = otherClinic.Id, Name = service.Name };
                var otherProvider = new Provider { ClinicId = otherClinic.Id, FirstName = "Sam", LastName = "Other" };
                db.ClinicalServices.Add(sameNameService);
                db.Providers.Add(otherProvider);
                await db.SaveChangesAsync();
                db.ProviderServices.Add(new ProviderService
                {
                    ProviderId = otherProvider.Id,
                    ClinicalServiceId = sameNameService.Id,
                    ClinicId = otherClinic.Id,
                });
                await db.SaveChangesAsync();
            }

            var referral = new Referral
            {
                PatientId = patient.Id,
                ClinicalServiceId = service.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Original referral reason",
            };

            if (operation == "ReadyWithProvider")
            {
                referral.AssignedProviderId = provider.Id;
            }

            if (operation == "ClearNote")
            {
                referral.Status = ReferralStatus.MissingInformation;
                referral.MissingInformationNote = "Original missing information note.";
            }

            db.Referrals.Add(referral);
            await db.SaveChangesAsync();

            var expectedSavedStatus = referral.Status;
            var expectedProviderId = referral.AssignedProviderId;
            var expectedNote = referral.MissingInformationNote;
            var referralUrl = $"/api/referrals/{referral.PublicId}";
            var requestedStatus = operation switch
            {
                "ReadyWithoutProvider" or "ReadyWithProvider" => "ReadyToSchedule",
                "ClearNote" => "PendingReview",
                "Close" => "Closed",
                _ => "MissingInformation",
            };

            using var request = operation switch
            {
                "List" => new HttpRequestMessage(HttpMethod.Get, "/api/referrals"),
                "Read" => new HttpRequestMessage(HttpMethod.Get, referralUrl),
                "Eligible" => new HttpRequestMessage(HttpMethod.Get, $"{referralUrl}/eligible-providers"),
                "Create" => new HttpRequestMessage(HttpMethod.Post, "/api/referrals")
                {
                    // Use another patient so the existing referral is not a duplicate.
                    Content = JsonContent.Create(new
                    {
                        patientPublicId = otherPatient.PublicId,
                        clinicalServicePublicId = service.PublicId,
                        referringProviderName = "Dr. Fictional Sender",
                        reason = "New referral reason",
                    }),
                },
                "Assign" => new HttpRequestMessage(HttpMethod.Put, $"{referralUrl}/provider")
                {
                    Content = JsonContent.Create(new { providerPublicId = provider.PublicId }),
                },
                "Status" or "ReadyWithoutProvider" or "ReadyWithProvider" or "ClearNote" or "Close"
                    => new HttpRequestMessage(HttpMethod.Put, $"{referralUrl}/status")
                    {
                        Content = JsonContent.Create(new
                        {
                            status = requestedStatus,
                            missingInformationNote = "  Awaiting test records.  ",
                        }),
                    },
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

            using var response = await client.SendAsync(request);
            Assert.Equal(expectedStatus, response.StatusCode);

            if (expectedStatus == HttpStatusCode.Unauthorized)
            {
                Assert.Contains(response.Headers.WwwAuthenticate, c => c.Scheme == "Bearer");
            }

            if (expectedStatus == HttpStatusCode.OK && operation == "Eligible")
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var eligible = Assert.Single(body.EnumerateArray());
                Assert.Equal(provider.PublicId, eligible.GetProperty("publicId").GetGuid());
            }
            else if (expectedStatus == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var returnedReferral = operation == "List"
                    ? Assert.Single(body.EnumerateArray())
                    : body;
                Assert.Equal(referral.PublicId, returnedReferral.GetProperty("publicId").GetGuid());

                if (operation == "List")
                {
                    Assert.Equal("Taylor", returnedReferral.GetProperty("patientFirstName").GetString());
                    Assert.Equal("Demo", returnedReferral.GetProperty("patientLastName").GetString());
                    Assert.Equal(service.Name, returnedReferral.GetProperty("serviceName").GetString());
                    Assert.Equal(clinic.Name, returnedReferral.GetProperty("clinicName").GetString());
                    Assert.Equal("PendingReview", returnedReferral.GetProperty("status").GetString());
                }
                else if (operation == "Read")
                {
                    Assert.Equal(patient.PublicId, body.GetProperty("patientPublicId").GetGuid());
                    Assert.Equal(service.PublicId, body.GetProperty("clinicalServicePublicId").GetGuid());
                    Assert.Equal(clinic.PublicId, body.GetProperty("clinicPublicId").GetGuid());
                    Assert.Equal(clinic.Name, body.GetProperty("clinicName").GetString());
                    Assert.Equal("Dr. Demo", body.GetProperty("referringProviderName").GetString());
                    Assert.Equal("Original referral reason", body.GetProperty("reason").GetString());
                    Assert.Equal("PendingReview", body.GetProperty("status").GetString());
                    Assert.Equal(JsonValueKind.Null, body.GetProperty("assignedProviderPublicId").ValueKind);
                    Assert.Equal(JsonValueKind.Null, body.GetProperty("missingInformationNote").ValueKind);
                }
                else if (operation == "Assign")
                {
                    expectedProviderId = provider.Id;
                    Assert.Equal(provider.PublicId, body.GetProperty("assignedProviderPublicId").GetGuid());
                }
                else
                {
                    expectedSavedStatus = operation switch
                    {
                        "Status" => ReferralStatus.MissingInformation,
                        "ReadyWithProvider" => ReferralStatus.ReadyToSchedule,
                        "ClearNote" => ReferralStatus.PendingReview,
                        "Close" => ReferralStatus.Closed,
                        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
                    };
                    expectedNote = operation == "Status" ? "Awaiting test records." : null;
                    Assert.Equal(expectedSavedStatus.ToString(), body.GetProperty("status").GetString());
                    Assert.Equal(expectedNote, body.GetProperty("missingInformationNote").GetString());
                }
            }

            // Query SQL Server again to verify writes and unchanged rejected requests.
            var savedReferrals = await db.Referrals.AsNoTracking().ToListAsync();
            var original = Assert.Single(savedReferrals, r => r.PublicId == referral.PublicId);
            Assert.Equal(patient.Id, original.PatientId);
            Assert.Equal(service.Id, original.ClinicalServiceId);
            Assert.Equal("Dr. Demo", original.ReferringProviderName);
            Assert.Equal("Original referral reason", original.Reason);
            Assert.Equal(referral.CreatedAtUtc, original.CreatedAtUtc);
            Assert.Equal(expectedSavedStatus, original.Status);
            Assert.Equal(expectedProviderId, original.AssignedProviderId);
            Assert.Equal(expectedNote, original.MissingInformationNote);

            if (expectedStatus == HttpStatusCode.Created)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var createdId = body.GetProperty("publicId").GetGuid();
                Assert.NotEqual(Guid.Empty, createdId);
                Assert.NotEqual(referral.PublicId, createdId);
                Assert.Equal("PendingReview", body.GetProperty("status").GetString());
                Assert.Equal($"/api/referrals/{createdId}", response.Headers.Location?.OriginalString);
                Assert.Equal(2, savedReferrals.Count);

                var created = Assert.Single(savedReferrals, r => r.PublicId == createdId);
                Assert.Equal(otherPatient.Id, created.PatientId);
                Assert.Equal(service.Id, created.ClinicalServiceId);
                Assert.Equal("Dr. Fictional Sender", created.ReferringProviderName);
                Assert.Equal("New referral reason", created.Reason);
                Assert.Equal(ReferralStatus.PendingReview, created.Status);
                Assert.Null(created.AssignedProviderId);
                Assert.Null(created.MissingInformationNote);
                Assert.Equal(created.CreatedAtUtc, body.GetProperty("createdAtUtc").GetDateTimeOffset());
            }
            else
            {
                Assert.Single(savedReferrals);
            }
        }
        finally
        {
            // Remove this test's temporary database, including after a failure.
            await db.Database.EnsureDeletedAsync();
        }
    }
}
