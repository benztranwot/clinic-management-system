using System.Data;
using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace HealthcareApp.Api.Workspaces;

public sealed class VisitorWorkspaceProvisioner(HealthcareDbContext source,
    IOptions<VisitorWorkspaceOptions> options, TimeProvider clock)
{
    private const int MaximumTemplateRowsPerTable = 10000;

    public async Task<bool> CreateAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var identity = AccountIdentity.FromPrincipal(user)
            ?? throw new InvalidOperationException("A verified identity is required.");
        if (!options.Value.Enabled)
            throw new WorkspaceProvisioningException(503, "workspace_unavailable", "Visitor workspaces are currently unavailable.");

        await using var transaction = await source.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize enrollment across API instances, including limits and duplicate requests.
        if (!await TransactionLock.TryAcquireAsync(source, "VisitorWorkspace:Provisioning", ct))
            throw new WorkspaceProvisioningException(409, "workspace_busy", "Another workspace is being prepared. Please try again shortly.", 5);

        if (await identity.QueryAccounts(source).IgnoreQueryFilters().AnyAsync(ct))
            return false;
        if (source.CurrentWorkspaceId != Guid.Empty)
            throw new InvalidOperationException("The demo template must come from the original workspace.");

        var now = clock.GetUtcNow();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var workspaces = source.Workspaces.IgnoreQueryFilters().Where(row => row.Id != Guid.Empty);
        if (await workspaces.CountAsync(ct) >= options.Value.MaximumWorkspaces)
            throw new WorkspaceProvisioningException(503, "workspace_limit", "New workspaces are temporarily unavailable. Please contact the application owner.");
        if (await workspaces.CountAsync(row => row.CreatedAtUtc >= dayStart, ct) >= options.Value.MaximumDailyWorkspaces)
            throw new WorkspaceProvisioningException(429, "workspace_daily_limit", "Today's workspace limit has been reached. Please try again tomorrow.",
                Math.Max(1, (int)Math.Ceiling((dayStart.AddDays(1) - now).TotalSeconds)));

        var clinics = await LoadAsync(source.Clinics, ct);
        var patients = await LoadAsync(source.Patients, ct);
        var providers = await LoadAsync(source.Providers, ct);
        var services = await LoadAsync(source.ClinicalServices, ct);
        var eligibility = await LoadAsync(source.ProviderServices, ct);
        var weeklyHours = await LoadAsync(source.ProviderWeeklyHours, ct);
        var timeOff = await LoadAsync(source.ProviderTimeOff, ct);
        var referrals = await LoadAsync(source.Referrals, ct);
        var appointments = await LoadAsync(source.Appointments, ct);
        if (clinics.Count == 0 || patients.Count == 0 || providers.Count == 0 || services.Count == 0)
            throw InvalidTemplate();

        var zones = new Dictionary<int, TimeZoneInfo>();
        foreach (var clinic in clinics)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(clinic.TimeZoneId, out var zone)) throw InvalidTemplate();
            zones.Add(clinic.Id, zone);
        }
        var providerClinics = providers.ToDictionary(row => row.Id, row => row.ClinicId);
        var confirmed = appointments.Where(row => row.Status == AppointmentStatus.Confirmed).ToList();
        // Shift by whole weeks to keep working weekdays and clinic-local times intact.
        var anchor = confirmed.Count > 0 ? confirmed.Max(row => row.StartUtc) : now;
        var shiftDays = confirmed.Count > 0
            ? checked((int)Math.Floor((now.AddDays(21).UtcDateTime.Date - anchor.UtcDateTime.Date).TotalDays / 7) * 7) : 0;
        DateTimeOffset Shift(DateTimeOffset value, int providerId)
        {
            var zone = zones[providerClinics[providerId]];
            var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(value, zone).DateTime.AddDays(shiftDays), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw InvalidTemplate();
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        }

        var workspace = new Workspace { CreatedAtUtc = now };
        source.Workspaces.Add(workspace);
        await source.SaveChangesAsync(ct);
        // Share the transaction, but use a separate context with an immutable destination scope.
        var destinationOptions = new DbContextOptionsBuilder<HealthcareDbContext>()
            .UseSqlServer(source.Database.GetDbConnection()).Options;
        await using var destination = new HealthcareDbContext(destinationOptions);
        destination.SelectWorkspace(workspace.Id);
        await destination.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);

        var clinicMap = clinics.ToDictionary(row => row.Id,
            row => new Clinic { Name = row.Name, Address = row.Address, TimeZoneId = row.TimeZoneId });
        var patientMap = patients.ToDictionary(row => row.Id,
            row => new Patient { FirstName = row.FirstName, LastName = row.LastName, DateOfBirth = row.DateOfBirth });
        destination.AddRange(clinicMap.Values);
        destination.AddRange(patientMap.Values);
        await destination.SaveChangesAsync(ct);

        var providerMap = providers.ToDictionary(row => row.Id, row => new Provider
        {
            ClinicId = clinicMap[row.ClinicId].Id, FirstName = row.FirstName, LastName = row.LastName,
        });
        var serviceMap = services.ToDictionary(row => row.Id, row => new ClinicalService
        {
            ClinicId = clinicMap[row.ClinicId].Id, Name = row.Name, AllowsDirectBooking = row.AllowsDirectBooking,
        });
        destination.AddRange(providerMap.Values);
        destination.AddRange(serviceMap.Values);
        await destination.SaveChangesAsync(ct);

        destination.ProviderServices.AddRange(eligibility.Select(row => new ProviderService
        {
            ClinicId = clinicMap[row.ClinicId].Id, ProviderId = providerMap[row.ProviderId].Id,
            ClinicalServiceId = serviceMap[row.ClinicalServiceId].Id,
        }));
        destination.ProviderWeeklyHours.AddRange(weeklyHours.Select(row => new ProviderWeeklyHours
        {
            ProviderId = providerMap[row.ProviderId].Id, DayOfWeek = row.DayOfWeek,
            StartMinute = row.StartMinute, EndMinute = row.EndMinute,
            BreakStartMinute = row.BreakStartMinute, BreakEndMinute = row.BreakEndMinute,
        }));
        await destination.SaveChangesAsync(ct);

        var accountId = new SqlParameter("@AccountId", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var firstProvider = providerMap.OrderBy(pair => pair.Key).First().Value;
        // This operation can create an owner only for a new, non-original workspace.
        await destination.Database.ExecuteSqlInterpolatedAsync($"""
            EXEC dbo.CreateVisitorWorkspaceAccount @WorkspaceId={workspace.Id}, @Issuer={identity.Issuer},
                @Subject={identity.Subject}, @ProviderId={firstProvider.Id}, @AccountId={accountId} OUTPUT;
            """, ct);
        var ownerId = (int)accountId.Value;

        var referralMap = referrals.ToDictionary(row => row.Id, row => new Referral
        {
            PatientId = patientMap[row.PatientId].Id, ClinicalServiceId = serviceMap[row.ClinicalServiceId].Id,
            AssignedProviderId = providerMap[row.AssignedProviderId].Id, ReferringProviderName = row.ReferringProviderName,
            Reason = row.Reason, Status = row.Status, MissingInformationNote = row.MissingInformationNote,
            CreatedAtUtc = Earlier(row.CreatedAtUtc.AddDays(shiftDays), now),
        });
        destination.AddRange(referralMap.Values);
        await destination.SaveChangesAsync(ct);

        foreach (var row in appointments)
        {
            var start = Shift(row.StartUtc, row.ProviderId);
            if (start.UtcTicks % TimeSpan.FromMinutes(Appointment.DurationMinutes).Ticks != 0) throw InvalidTemplate();
            destination.Appointments.Add(new Appointment
            {
                PatientId = patientMap[row.PatientId].Id, ClinicalServiceId = serviceMap[row.ClinicalServiceId].Id,
                ProviderId = providerMap[row.ProviderId].Id, ReferralId = row.ReferralId is int id ? referralMap[id].Id : null,
                StartUtc = start, Status = row.Status, BookedByAppAccountId = ownerId,
                CreatedAtUtc = Earlier(row.CreatedAtUtc.AddDays(shiftDays), now),
            });
        }
        foreach (var row in timeOff)
        {
            var start = Shift(row.StartUtc, row.ProviderId);
            var end = Shift(row.EndUtc, row.ProviderId);
            if (end <= start) throw InvalidTemplate();
            destination.ProviderTimeOff.Add(new ProviderTimeOff { ProviderId = providerMap[row.ProviderId].Id, StartUtc = start, EndUtc = end });
        }
        await destination.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static async Task<List<T>> LoadAsync<T>(IQueryable<T> rows, CancellationToken ct) where T : class
    {
        var result = await rows.AsNoTracking().Take(MaximumTemplateRowsPerTable + 1).ToListAsync(ct);
        if (result.Count > MaximumTemplateRowsPerTable) throw InvalidTemplate();
        return result;
    }

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
    private static WorkspaceProvisioningException InvalidTemplate() => new(503, "workspace_template_unavailable",
        "The demo workspace is being prepared. Please try again later.");
}
