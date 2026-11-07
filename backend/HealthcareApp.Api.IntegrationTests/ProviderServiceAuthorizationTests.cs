using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ProviderServiceAuthorizationTests
{
    [Theory]
    [InlineData("List", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("List", true, null, HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:services", HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:provider-services", HttpStatusCode.OK)]
    [InlineData("Add", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Add", true, "read:provider-services", HttpStatusCode.Forbidden)]
    [InlineData("Add", true, "manage:provider-services", HttpStatusCode.Created)]
    [InlineData("Remove", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Remove", true, "read:provider-services", HttpStatusCode.Forbidden)]
    [InlineData("Remove", true, "manage:provider-services", HttpStatusCode.NoContent)]
    [InlineData("AddDuplicate", true, "manage:provider-services", HttpStatusCode.Conflict)]
    [InlineData("AddCrossClinic", true, "manage:provider-services", HttpStatusCode.BadRequest)]
    [InlineData("RemoveReferenced", true, "manage:provider-services", HttpStatusCode.Conflict)]
    public async Task ProviderServiceRoutes_AccessAndRelationshipRules_ReturnExpectedStatus(
        string operation,
        bool sendToken,
        string? permission,
        HttpStatusCode expectedStatus
    )
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();

        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Use the shared token configuration with real JWT validation.
                tokens.ConfigureServices(services);

                if (operation is "AddDuplicate" or "RemoveReferenced")
                {
                    // These cases deliberately trigger database constraint failures.
                    services.ConfigureDbContext<HealthcareDbContext>(options =>
                        options.ConfigureWarnings(warnings =>
                            warnings.Ignore(
                                CoreEventId.SaveChangesFailed,
                                RelationalEventId.CommandError
                            )
                        )
                    );
                }
            });
        });

        using var client = configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the database target before creating or deleting anything.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "Eligibility Authorization Clinic" };
            var otherClinic = new Clinic { Name = "Other Eligibility Test Clinic" };
            db.Clinics.AddRange(clinic, otherClinic);
            await db.SaveChangesAsync();

            // Two providers share an existing service at the same clinic.
            var provider = new Provider
            {
                ClinicId = clinic.Id,
                FirstName = "Taylor",
                LastName = "Demo",
            };
            var otherProvider = new Provider
            {
                ClinicId = clinic.Id,
                FirstName = "Casey",
                LastName = "Other",
            };
            var existingService = new ClinicalService
            {
                ClinicId = clinic.Id,
                Name = "Physical exam",
            };
            var additionalService = new ClinicalService
            {
                ClinicId = clinic.Id,
                Name = "Hearing assessment",
            };
            var otherClinicService = new ClinicalService
            {
                ClinicId = otherClinic.Id,
                Name = "Physical exam",
            };

            db.Providers.AddRange(provider, otherProvider);
            db.ClinicalServices.AddRange(existingService, additionalService, otherClinicService);
            await db.SaveChangesAsync();

            db.ProviderServices.AddRange(
                new ProviderService
                {
                    ProviderId = provider.Id,
                    ClinicalServiceId = existingService.Id,
                    ClinicId = clinic.Id,
                },
                new ProviderService
                {
                    ProviderId = otherProvider.Id,
                    ClinicalServiceId = existingService.Id,
                    ClinicId = clinic.Id,
                }
            );
            await db.SaveChangesAsync();

            if (operation == "RemoveReferenced")
            {
                // A referral references the exact provider/service pair being removed.
                var patient = new Patient
                {
                    FirstName = "Morgan",
                    LastName = "Demo",
                    DateOfBirth = new DateOnly(1990, 5, 10),
                };
                db.Patients.Add(patient);
                await db.SaveChangesAsync();

                db.Referrals.Add(
                    new Referral
                    {
                        PatientId = patient.Id,
                        ClinicalServiceId = existingService.Id,
                        AssignedProviderId = provider.Id,
                        ReferringProviderName = "Dr. Demo",
                        Reason = "Eligibility relationship test",
                    }
                );
                await db.SaveChangesAsync();
            }

            if (sendToken)
            {
                // Give this request exactly the permissions specified by the test case.
                string[] permissions = permission is null ? [] : [permission];
                tokens.Authorize(client, permissions);
            }

            var collectionUrl = $"/api/providers/{provider.PublicId}/services";
            var linkUrl = $"{collectionUrl}/{existingService.PublicId}";
            var requestedServiceId = operation switch
            {
                "AddDuplicate" => existingService.PublicId,
                "AddCrossClinic" => otherClinicService.PublicId,
                _ => additionalService.PublicId,
            };

            using var request = operation switch
            {
                "List" => new HttpRequestMessage(HttpMethod.Get, collectionUrl),
                "Add" or "AddDuplicate" or "AddCrossClinic" => new HttpRequestMessage(
                    HttpMethod.Post,
                    collectionUrl
                )
                {
                    Content = JsonContent.Create(
                        new { clinicalServicePublicId = requestedServiceId }
                    ),
                },
                "Remove" or "RemoveReferenced" => new HttpRequestMessage(
                    HttpMethod.Delete,
                    linkUrl
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

            using var response = await client.SendAsync(request);
            Assert.Equal(expectedStatus, response.StatusCode);

            if (expectedStatus == HttpStatusCode.Unauthorized)
            {
                Assert.Contains(response.Headers.WwwAuthenticate, c => c.Scheme == "Bearer");
            }

            if (expectedStatus == HttpStatusCode.OK)
            {
                // Return only this provider's linked service, once.
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var returnedService = Assert.Single(body.EnumerateArray());
                Assert.Equal(
                    existingService.PublicId,
                    returnedService.GetProperty("publicId").GetGuid()
                );
                Assert.Equal(existingService.Name, returnedService.GetProperty("name").GetString());
            }

            // Inspect the stored links rather than tracked changes in the arrange context.
            var links = await db.ProviderServices.AsNoTracking().ToListAsync();
            var untouchedLink = Assert.Single(
                links,
                link =>
                    link.ProviderId == otherProvider.Id
                    && link.ClinicalServiceId == existingService.Id
            );
            Assert.Equal(clinic.Id, untouchedLink.ClinicId);

            if (expectedStatus == HttpStatusCode.Created)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(additionalService.PublicId, body.GetProperty("publicId").GetGuid());
                Assert.Equal(additionalService.Name, body.GetProperty("name").GetString());
                Assert.Equal(3, links.Count);
                var addedLink = Assert.Single(
                    links,
                    link =>
                        link.ProviderId == provider.Id
                        && link.ClinicalServiceId == additionalService.Id
                );
                Assert.Equal(clinic.Id, addedLink.ClinicId);
            }
            else if (expectedStatus == HttpStatusCode.NoContent)
            {
                Assert.Empty(await response.Content.ReadAsStringAsync());
                Assert.Single(links);
            }
            else
            {
                // Denied or invalid requests must preserve both original links.
                Assert.Equal(2, links.Count);
            }

            if (expectedStatus != HttpStatusCode.NoContent)
            {
                var originalLink = Assert.Single(
                    links,
                    link =>
                        link.ProviderId == provider.Id
                        && link.ClinicalServiceId == existingService.Id
                );
                Assert.Equal(clinic.Id, originalLink.ClinicId);
            }

            // Link operations must not delete the provider or the service itself.
            Assert.Equal(2, await db.Providers.CountAsync());
            Assert.Equal(3, await db.ClinicalServices.CountAsync());

            if (operation == "RemoveReferenced")
            {
                var savedReferral = await db.Referrals.AsNoTracking().SingleAsync();
                Assert.Equal(provider.Id, savedReferral.AssignedProviderId);
                Assert.Equal(existingService.Id, savedReferral.ClinicalServiceId);
            }
        }
        finally
        {
            // Remove this test's temporary database, including after a failure.
            await db.Database.EnsureDeletedAsync();
        }
    }
}
