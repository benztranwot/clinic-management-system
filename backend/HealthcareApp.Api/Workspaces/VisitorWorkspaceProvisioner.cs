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
            throw new InvalidOperationException("Only an unlinked identity can create a visitor workspace.");

        var now = clock.GetUtcNow();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var workspaces = source.Workspaces.IgnoreQueryFilters().Where(row => row.Id != Guid.Empty && row.Id != Workspace.VisitorTemplateId);
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
        // Prefer a whole-week shift that keeps the latest confirmed visit about three weeks ahead.
        var anchor = confirmed.Count > 0 ? confirmed.Max(row => row.StartUtc) : now;
        var preferredShift = confirmed.Count > 0
            ? checked((int)Math.Floor((now.AddDays(21).UtcDateTime.Date - anchor.UtcDateTime.Date).TotalDays / 7) * 7) : 0;

        TimeZoneInfo ZoneFor(int providerId) => zones[providerClinics[providerId]];

        var shiftDays = preferredShift;
        if (!HasDistinctConfirmedSlots(preferredShift))
        {
            // A DST adjustment can put two confirmed visits in the same slot.
            // Try nearby weeks while keeping the latest visit within the 30-day demo window.
            shiftDays = new[] { preferredShift - 7, preferredShift + 7, preferredShift - 14 }
                .Where(days => anchor.AddDays(days) > now && anchor.AddDays(days) <= now.AddDays(29))
                .Where(HasDistinctConfirmedSlots).Select(days => (int?)days).FirstOrDefault()
                ?? throw InvalidTemplate();
        }

        bool HasDistinctConfirmedSlots(int days)
        {
            var providerSlots = new HashSet<(int, DateTimeOffset)>();
            var patientSlots = new HashSet<(int, DateTimeOffset)>();
            foreach (var visit in confirmed)
            {
                var start = Shift(visit.StartUtc, ZoneFor(visit.ProviderId), days);
                if (!providerSlots.Add((visit.ProviderId, start)) || !patientSlots.Add((visit.PatientId, start)))
                    return false;
            }
            return true;
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
            var start = Shift(row.StartUtc, ZoneFor(row.ProviderId), shiftDays);
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
            var zone = ZoneFor(row.ProviderId);
            var start = Shift(row.StartUtc, zone, shiftDays);
            var end = Shift(row.EndUtc, zone, shiftDays);
            var sourceStart = TimeZoneInfo.ConvertTime(row.StartUtc, zone).DateTime;
            var sourceEnd = TimeZoneInfo.ConvertTime(row.EndUtc, zone).DateTime;
            // Preserve the elapsed block when a repeated source hour loses one of its occurrences.
            if (zone.IsAmbiguousTime(sourceStart) || zone.IsAmbiguousTime(sourceEnd) || end <= start)
                end = start + (row.EndUtc - row.StartUtc);
            destination.ProviderTimeOff.Add(new ProviderTimeOff { ProviderId = providerMap[row.ProviderId].Id, StartUtc = start, EndUtc = end });
        }
        await destination.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static DateTimeOffset Shift(DateTimeOffset value, TimeZoneInfo zone, int days)
    {
        if (days == 0) return value.ToUniversalTime();
        var sourceLocal = TimeZoneInfo.ConvertTime(value, zone);
        var destination = sourceLocal.DateTime.AddDays(days);
        if (zone.IsInvalidTime(destination))
        {
            // Move forward by the offset change, keeping the minute within the missing hour.
            var before = destination.AddMinutes(-1);
            while (zone.IsInvalidTime(before)) before = before.AddMinutes(-1);
            var after = destination.AddMinutes(1);
            while (zone.IsInvalidTime(after)) after = after.AddMinutes(1);
            destination += zone.GetUtcOffset(after) - zone.GetUtcOffset(before);
        }
        if (zone.IsAmbiguousTime(destination))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(destination);
            var offset = offsets.Contains(sourceLocal.Offset) ? sourceLocal.Offset : offsets.Max();
            return new DateTimeOffset(destination, offset).ToUniversalTime();
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(destination, zone));
    }

    private static async Task<List<T>> LoadAsync<T>(IQueryable<T> rows, CancellationToken ct)
        where T : class, IWorkspaceRecord
    {
        // This reserved scope is seeded from fictional literals and cannot be opened by an API account.
        var result = await rows.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.WorkspaceId == Workspace.VisitorTemplateId)
            .Take(MaximumTemplateRowsPerTable + 1).ToListAsync(ct);
        if (result.Count > MaximumTemplateRowsPerTable) throw InvalidTemplate();
        return result;
    }

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
    private static WorkspaceProvisioningException InvalidTemplate() => new(503, "workspace_template_unavailable",
        "The demo workspace is being prepared. Please try again later.");
}
