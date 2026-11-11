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
        routes.MapPost("/{publicId:guid}/cancel", CancelAsync);
        routes.MapPost("/{publicId:guid}/complete", CompleteAsync);

        app.MapPost("/api/referrals/{referralPublicId:guid}/appointments", BookReferredAsync)
            .RequireAuthorization();
    }

    private static Task<IResult> BookDirectAsync(
        CreateDirectAppointmentRequest request,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    ) => BookAsync(
        null, request.PatientPublicId, request.ClinicalServicePublicId,
        request.ProviderPublicId, request.StartUtc, user, db, clock, cancellationToken
    );

    private static Task<IResult> BookReferredAsync(
        Guid referralPublicId,
        CreateReferredAppointmentRequest request,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    ) => BookAsync(
        referralPublicId, null, null, request.ProviderPublicId,
        request.StartUtc, user, db, clock, cancellationToken
    );

    private static async Task<IResult> BookAsync(
        Guid? referralPublicId,
        Guid? patientPublicId,
        Guid? clinicalServicePublicId,
        Guid? providerPublicId,
        string? startUtc,
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

        if (providerPublicId == Guid.Empty || referralPublicId == Guid.Empty
            || (referralPublicId is null
                && (providerPublicId is null || patientPublicId == Guid.Empty || clinicalServicePublicId == Guid.Empty)))
        {
            return Results.BadRequest(new { message = "Select the records required for this booking." });
        }

        if (!UtcTimestamp.TryParse(startUtc, out var start)
            || start.Ticks % (Appointment.DurationMinutes * TimeSpan.TicksPerMinute) != 0)
        {
            return Results.BadRequest(new { message = "Choose a UTC start on the 30-minute grid." });
        }

        // A referred booking uses the patient, service, and provider from the reviewed referral.
        var sources = referralPublicId is not null
            ? from referral in db.Referrals
              join patient in db.Patients on referral.PatientId equals patient.Id
              join service in db.ClinicalServices on referral.ClinicalServiceId equals service.Id
              where referral.PublicId == referralPublicId
              select new
              {
                  PatientId = patient.Id,
                  PatientPublicId = patient.PublicId,
                  ServiceId = service.Id,
                  ServicePublicId = service.PublicId,
                  service.ClinicId,
                  service.AllowsDirectBooking,
                  ReferralId = (int?)referral.Id,
                  AssignedProviderId = (int?)referral.AssignedProviderId,
                  ReviewStatus = (ReferralStatus?)referral.Status,
              }
            : from patient in db.Patients
              from service in db.ClinicalServices
              where patient.PublicId == patientPublicId && service.PublicId == clinicalServicePublicId
              select new
              {
                  PatientId = patient.Id,
                  PatientPublicId = patient.PublicId,
                  ServiceId = service.Id,
                  ServicePublicId = service.PublicId,
                  service.ClinicId,
                  service.AllowsDirectBooking,
                  ReferralId = (int?)null,
                  AssignedProviderId = (int?)null,
                  ReviewStatus = (ReferralStatus?)null,
              };

        var accounts = identity.QueryAccounts(db);
        var targets =
            from source in sources
            join clinic in db.Clinics on source.ClinicId equals clinic.Id
            join provider in db.Providers on clinic.Id equals provider.ClinicId
            from account in accounts
            where (referralPublicId != null ? provider.Id == source.AssignedProviderId : provider.PublicId == providerPublicId)
                && (account.IsAdministrator || db.ClinicMemberships.Any(membership =>
                    membership.AppAccountId == account.Id
                    && membership.ClinicId == clinic.Id
                    && membership.Role == ClinicStaffRole.FrontDesk))
            select new
            {
                source.PatientId,
                source.PatientPublicId,
                source.ServiceId,
                source.ServicePublicId,
                source.ReferralId,
                source.ReviewStatus,
                source.AllowsDirectBooking,
                ProviderId = provider.Id,
                ProviderPublicId = provider.PublicId,
                AccountId = account.Id,
                clinic.TimeZoneId,
                Eligible = db.ProviderServices.Any(eligibility =>
                    eligibility.ProviderId == provider.Id && eligibility.ClinicalServiceId == source.ServiceId
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
            // Retain read locks on eligibility and booking policy until the write commits.
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken
            );

            // Use the same referral lock as the review and assignment routes.
            if (referralPublicId is Guid referralKey
                && !await TransactionLock.TryAcquireReferralAsync(db, referralKey, cancellationToken))
            {
                return Results.Conflict(new
                {
                    code = "referral_busy",
                    message = "The referral is being updated. Try again.",
                });
            }

            if (!await ProviderCalendarLock.TryAcquireAsync(db, providerId.Value, cancellationToken))
            {
                return SlotConflict();
            }

            var target = await targets.SingleOrDefaultAsync(
                item => item.ProviderId == providerId.Value, cancellationToken
            );
            if (target is null)
            {
                return referralPublicId is not null && await targets.AnyAsync(cancellationToken)
                    ? ProviderChanged()
                    : Results.NotFound();
            }

            if (target.ReferralId is int referralId)
            {
                if (providerPublicId is Guid requestedProvider && requestedProvider != target.ProviderPublicId)
                {
                    return ProviderChanged();
                }

                if (target.ReviewStatus != ReferralStatus.ReadyToSchedule)
                {
                    return Results.Conflict(new
                    {
                        code = "referral_not_ready",
                        message = "Mark this referral ready to schedule before booking.",
                    });
                }

                if (await db.Appointments.AnyAsync(appointment =>
                        appointment.ReferralId == referralId
                        && appointment.Status != AppointmentStatus.Cancelled,
                        cancellationToken))
                {
                    return ReferralConflict();
                }
            }
            else if (!target.AllowsDirectBooking)
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
                    appointment.PatientId == target.PatientId
                    && appointment.Status == AppointmentStatus.Confirmed
                    && appointment.StartUtc == start,
                    cancellationToken))
            {
                return PatientConflict();
            }

            var booking = new Appointment
            {
                PatientId = target.PatientId,
                ClinicalServiceId = target.ServiceId,
                ProviderId = target.ProviderId,
                ReferralId = target.ReferralId,
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
                    target.PatientPublicId,
                    ClinicalServicePublicId = target.ServicePublicId,
                    target.ProviderPublicId,
                    ReferralPublicId = referralPublicId,
                    booking.StartUtc,
                    EndUtc = booking.StartUtc.AddMinutes(Appointment.DurationMinutes),
                    Status = booking.Status.ToString(),
                }
            );
        }
        catch (Exception exception) when (IsDuplicateIndex(exception, "UX_Appointments_NonCancelledReferral"))
        {
            return ReferralConflict();
        }
        catch (Exception exception) when (IsDuplicateIndex(exception, "UX_Appointments_ConfirmedPatientSlot"))
        {
            return PatientConflict();
        }
        catch (Exception exception) when (
            IsDuplicateIndex(exception, "UX_Appointments_ConfirmedProviderSlot")
            || GetSqlException(exception) is { Number: 1205 })
        {
            return SlotConflict();
        }
    }

    private static Task<IResult> CancelAsync(
        Guid publicId,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    ) => UpdateStatusAsync(publicId, AppointmentStatus.Cancelled, user, db, clock, cancellationToken);

    private static Task<IResult> CompleteAsync(
        Guid publicId,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    ) => UpdateStatusAsync(publicId, AppointmentStatus.Completed, user, db, clock, cancellationToken);

    private static async Task<IResult> UpdateStatusAsync(
        Guid publicId,
        AppointmentStatus status,
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

        var administrators = identity.QueryAccounts(db).Where(account => account.IsAdministrator);
        var ownProviders = identity.QueryOwnProviders(db);
        var schedulingClinics = identity.QuerySchedulingClinics(db);
        var appointments =
            from appointment in db.Appointments
            join service in db.ClinicalServices on appointment.ClinicalServiceId equals service.Id
            where appointment.PublicId == publicId
                && (status == AppointmentStatus.Cancelled
                    ? schedulingClinics.Any(clinic => clinic.Id == service.ClinicId)
                    : administrators.Any() || ownProviders.Any(provider => provider.Id == appointment.ProviderId))
            select appointment;

        var target = await appointments.Select(appointment => new
        {
            appointment.Id,
            appointment.ProviderId,
            ReferralPublicId = db.Referrals.Where(referral => referral.Id == appointment.ReferralId)
                .Select(referral => (Guid?)referral.PublicId).FirstOrDefault(),
        }).SingleOrDefaultAsync(cancellationToken);

        if (target is null)
        {
            return Results.NotFound();
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Referred bookings always lock the referral before the provider calendar.
            if (target.ReferralPublicId is Guid referralPublicId
                && !await TransactionLock.TryAcquireReferralAsync(db, referralPublicId, cancellationToken))
            {
                return AppointmentChanged();
            }

            if (!await ProviderCalendarLock.TryAcquireAsync(db, target.ProviderId, cancellationToken))
            {
                return AppointmentChanged();
            }

            var appointment = await appointments.SingleOrDefaultAsync(
                item => item.Id == target.Id && item.ProviderId == target.ProviderId, cancellationToken
            );
            if (appointment is null)
            {
                return AppointmentChanged();
            }

            // Retrying a completed action returns the same result without changing the referral again.
            if (appointment.Status == status)
            {
                return Results.Ok(new { publicId, Status = status.ToString() });
            }

            if (appointment.Status != AppointmentStatus.Confirmed)
            {
                return Results.Conflict(new
                {
                    code = "appointment_not_confirmed",
                    message = "Only a confirmed appointment can be cancelled or completed.",
                });
            }

            if (status == AppointmentStatus.Completed && appointment.StartUtc > clock.GetUtcNow())
            {
                return Results.Conflict(new
                {
                    code = "appointment_not_started",
                    message = "The appointment can be completed once it has started.",
                });
            }

            appointment.Status = status;
            await db.SaveChangesAsync(cancellationToken);

            if (status == AppointmentStatus.Completed && appointment.ReferralId is int referralId)
            {
                // Close the referral in the same transaction as the completed visit.
                await db.Referrals.Where(referral => referral.Id == referralId).ExecuteUpdateAsync(
                    update => update.SetProperty(referral => referral.Status, ReferralStatus.Closed)
                        .SetProperty(referral => referral.MissingInformationNote, (string?)null),
                    cancellationToken
                );
            }

            // A cancelled appointment releases its slot. Its referral already has a ready review.
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { publicId, Status = status.ToString() });
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppointmentChanged();
        }
        catch (Exception exception) when (GetSqlException(exception) is { Number: 1205 })
        {
            return AppointmentChanged();
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
                ReferralPublicId = db.Referrals.Where(referral => referral.Id == appointment.ReferralId)
                    .Select(referral => (Guid?)referral.PublicId).FirstOrDefault(),
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
            booking.ReferralPublicId,
            booking.StartUtc,
            EndUtc = booking.StartUtc.AddMinutes(Appointment.DurationMinutes),
            Status = booking.Status.ToString(),
        });
    }

    private static SqlException? GetSqlException(Exception exception) =>
        exception as SqlException ?? exception.InnerException as SqlException;

    private static bool IsDuplicateIndex(Exception exception, string index) =>
        GetSqlException(exception) is { Number: 2601 or 2627 } sql
        && sql.Message.Contains(index, StringComparison.Ordinal);

    private static IResult ProviderChanged() => Results.Conflict(new
    {
        code = "referral_provider_changed",
        message = "Use the referral's assigned provider. Reload the referral and choose a slot.",
    });

    private static IResult AppointmentChanged() => Results.Conflict(new
    {
        code = "appointment_changed",
        message = "The appointment is being updated or has changed. Reload it and try again.",
    });

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

    private static IResult ReferralConflict() => Results.Conflict(new
    {
        code = "referral_already_booked",
        message = "This referral already has a booking.",
    });
}
