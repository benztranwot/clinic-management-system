using System.Data;
using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using HealthcareApp.Api.Scheduling;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class AppointmentEndpoints
{
    public static void MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/appointments").RequireAuthorization();
        routes.MapPost("", BookDirectAsync);
        routes.MapGet("/{publicId:guid}", GetByIdAsync);
    }

    private static async Task<IResult> BookDirectAsync(
        CreateDirectAppointmentRequest request,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        if (request.PatientPublicId == Guid.Empty
            || request.ClinicalServicePublicId == Guid.Empty
            || request.ProviderPublicId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Select a patient, service, and provider." });
        }

        if (!UtcTimestamp.TryParse(request.StartUtc, out var start)
            || start.Ticks % (Appointment.DurationMinutes * TimeSpan.TicksPerMinute) != 0)
        {
            return Results.BadRequest(new { message = "Choose a UTC start on the 30-minute grid." });
        }

        var accounts = identity.QueryAccounts(db);
        var targets =
            from provider in db.Providers
            join clinic in db.Clinics on provider.ClinicId equals clinic.Id
            join service in db.ClinicalServices on clinic.Id equals service.ClinicId
            from account in accounts
            where provider.PublicId == request.ProviderPublicId
                && service.PublicId == request.ClinicalServicePublicId
                && (account.IsAdministrator || db.ClinicMemberships.Any(membership =>
                    membership.AppAccountId == account.Id
                    && membership.ClinicId == clinic.Id
                    && membership.Role == ClinicStaffRole.FrontDesk))
            select new
            {
                ProviderId = provider.Id,
                ServiceId = service.Id,
                AccountId = account.Id,
                clinic.TimeZoneId,
                service.AllowsDirectBooking,
                Eligible = db.ProviderServices.Any(eligibility =>
                    eligibility.ProviderId == provider.Id && eligibility.ClinicalServiceId == service.Id
                ),
            };

        var providerId = await targets.Select(target => (int?)target.ProviderId)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerId is null)
        {
            return Results.NotFound();
        }

        try
        {
            // Retain read locks on the booking policy and eligibility until the write commits.
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken
            );
            if (!await ProviderCalendarLock.TryAcquireAsync(db, providerId.Value, cancellationToken))
            {
                return SlotConflict();
            }

            // Recheck clinic scope, service policy, and provider eligibility under the lock.
            var target = await targets.SingleOrDefaultAsync(
                item => item.ProviderId == providerId.Value, cancellationToken
            );
            if (target is null)
            {
                return Results.NotFound();
            }

            if (!target.AllowsDirectBooking)
            {
                return Results.Conflict(new
                {
                    code = "referral_required",
                    message = "This service requires a referral.",
                });
            }

            if (!target.Eligible)
            {
                return Results.BadRequest(new { message = "This provider does not offer the selected service." });
            }

            if (string.IsNullOrEmpty(target.TimeZoneId)
                || !TimeZoneInfo.TryFindSystemTimeZoneById(target.TimeZoneId, out var timeZone))
            {
                return Results.Conflict(new
                {
                    code = "clinic_time_zone_required",
                    message = "Set the clinic's time zone before booking.",
                });
            }

            var patientId = await db.Patients
                .Where(patient => patient.PublicId == request.PatientPublicId)
                .Select(patient => (int?)patient.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (patientId is null)
            {
                return Results.NotFound();
            }

            var now = clock.GetUtcNow();
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, timeZone).DateTime);
            if (start <= now || !AppointmentAvailability.IsInBookingWindow(localDate, timeZone, now))
            {
                return Results.BadRequest(new { message = "Choose a future slot within 30 days." });
            }

            var slots = await AppointmentAvailability.FindAsync(
                db, target.ProviderId, localDate, timeZone, now, cancellationToken
            );
            if (!slots.Any(slot => slot.StartUtc == start))
            {
                return SlotConflict();
            }

            if (await db.Appointments.AnyAsync(appointment =>
                    appointment.PatientId == patientId.Value
                    && appointment.Status == AppointmentStatus.Confirmed
                    && appointment.StartUtc == start,
                    cancellationToken))
            {
                return PatientConflict();
            }

            var booking = new Appointment
            {
                PatientId = patientId.Value,
                ClinicalServiceId = target.ServiceId,
                ProviderId = target.ProviderId,
                BookedByAppAccountId = target.AccountId,
                StartUtc = start,
                CreatedAtUtc = now,
            };
            db.Appointments.Add(booking);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Created(
                $"/api/appointments/{booking.PublicId}",
                new
                {
                    booking.PublicId,
                    request.PatientPublicId,
                    request.ClinicalServicePublicId,
                    request.ProviderPublicId,
                    booking.StartUtc,
                    EndUtc = booking.StartUtc.AddMinutes(Appointment.DurationMinutes),
                    Status = booking.Status.ToString(),
                }
            );
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("UX_Appointments_ConfirmedPatientSlot", StringComparison.Ordinal))
        {
            return PatientConflict();
        }
        catch (Exception exception) when (IsCalendarConflict(exception))
        {
            return SlotConflict();
        }
    }

    private static async Task<IResult> GetByIdAsync(
        Guid publicId,
        ClaimsPrincipal user,
        HttpContext http,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var booking = await (
            from appointment in db.Appointments
            join patient in db.Patients on appointment.PatientId equals patient.Id
            join service in db.ClinicalServices on appointment.ClinicalServiceId equals service.Id
            join provider in db.Providers on appointment.ProviderId equals provider.Id
            join clinic in identity.QueryStaffClinics(db) on service.ClinicId equals clinic.Id
            where appointment.PublicId == publicId
            select new
            {
                appointment.PublicId,
                PatientPublicId = patient.PublicId,
                ClinicalServicePublicId = service.PublicId,
                ProviderPublicId = provider.PublicId,
                appointment.StartUtc,
                appointment.Status,
            }
        ).SingleOrDefaultAsync(cancellationToken);

        if (booking is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            booking.PublicId,
            booking.PatientPublicId,
            booking.ClinicalServicePublicId,
            booking.ProviderPublicId,
            booking.StartUtc,
            EndUtc = booking.StartUtc.AddMinutes(Appointment.DurationMinutes),
            Status = booking.Status.ToString(),
        });
    }

    private static bool IsCalendarConflict(Exception exception)
    {
        var sql = exception as SqlException ?? exception.InnerException as SqlException;
        return sql is { Number: 1205 }
            || (sql is { Number: 2601 or 2627 }
                && sql.Message.Contains("UX_Appointments_ConfirmedProviderSlot", StringComparison.Ordinal));
    }

    private static IResult SlotConflict() => Results.Conflict(new
    {
        code = "slot_unavailable",
        message = "This slot is no longer available. Choose another time.",
    });

    private static IResult PatientConflict() => Results.Conflict(new
    {
        code = "patient_already_booked",
        message = "This patient already has an appointment at that time.",
    });
}
