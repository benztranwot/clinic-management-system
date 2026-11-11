-- Seed fictional records for the local referral workflow.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

-- Allow the development database and the database used to verify this script.
IF DB_NAME() NOT IN (N'HealthcareApp_Dev', N'HealthcareApp_SeedCheck')
    THROW 51000, 'Run this seed against a named development or seed-check database.', 1;

-- Keep the demo IDs stable so the same requests work after a reset.
DECLARE @ClinicPublicId uniqueidentifier = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
DECLARE @AlexPublicId uniqueidentifier = '11111111-1111-4111-8111-111111111111';
DECLARE @JamiePublicId uniqueidentifier = '99999999-9999-4999-8999-999999999999';
DECLARE @RileyPublicId uniqueidentifier = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
DECLARE @BethPublicId uniqueidentifier = '33333333-3333-4333-8333-333333333333';
DECLARE @JohnPublicId uniqueidentifier = '44444444-4444-4444-8444-444444444444';
DECLARE @ExamPublicId uniqueidentifier = '22222222-2222-4222-8222-222222222222';
DECLARE @TherapyPublicId uniqueidentifier = '55555555-5555-4555-8555-555555555555';
DECLARE @ClosedReferralPublicId uniqueidentifier = '77777777-7777-4777-8777-777777777777';
DECLARE @PendingReferralPublicId uniqueidentifier = '88888888-8888-4888-8888-888888888888';
DECLARE @ReadyReferralPublicId uniqueidentifier = '66666666-6666-4666-8666-666666666666';
DECLARE @MissingInfoReferralPublicId uniqueidentifier = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
DECLARE @ScheduledReferralPublicId uniqueidentifier = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
DECLARE @AppointmentPublicId uniqueidentifier = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee';

-- Save all the seed rows together and roll back if an insert fails.
BEGIN TRANSACTION;

-- Insert Northside Demo Clinic if the demo clinic ID is missing.
IF NOT EXISTS (SELECT 1 FROM dbo.Clinics WHERE PublicId = @ClinicPublicId)
BEGIN
    INSERT INTO dbo.Clinics (PublicId, Name, Address, TimeZoneId)
    VALUES (@ClinicPublicId, N'Northside Demo Clinic', N'100 Demo Avenue', N'America/Chicago');
END;

-- Find the clinic's internal ID for the provider and service foreign keys.
DECLARE @ClinicId int = (SELECT Id FROM dbo.Clinics WHERE PublicId = @ClinicPublicId);

-- Insert the fictional patients.
INSERT INTO dbo.Patients (PublicId, FirstName, LastName, DateOfBirth)
SELECT demo.PublicId, demo.FirstName, demo.LastName, demo.DateOfBirth
FROM (VALUES
    (@AlexPublicId, N'Alex', N'Demo', DATEFROMPARTS(2000, 1, 1)),
    (@JamiePublicId, N'Jamie', N'Demo', DATEFROMPARTS(1998, 4, 12)),
    (@RileyPublicId, N'Riley', N'Demo', DATEFROMPARTS(1994, 6, 18))
) AS demo (PublicId, FirstName, LastName, DateOfBirth)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Patients AS p WHERE p.PublicId = demo.PublicId);

-- Insert Beth and John as providers at Northside.
INSERT INTO dbo.Providers (PublicId, ClinicId, FirstName, LastName)
SELECT demo.PublicId, @ClinicId, demo.FirstName, demo.LastName
FROM (VALUES
    (@BethPublicId, N'Beth', N'Demo'),
    (@JohnPublicId, N'John', N'Demo')
) AS demo (PublicId, FirstName, LastName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Providers AS p WHERE p.PublicId = demo.PublicId);

-- Insert the physical exam and therapy services at Northside.
INSERT INTO dbo.ClinicalServices (PublicId, ClinicId, Name, AllowsDirectBooking)
SELECT demo.PublicId, @ClinicId, demo.Name, demo.AllowsDirectBooking
FROM (VALUES
    (@ExamPublicId, N'Physical exam', 1),
    (@TherapyPublicId, N'Therapy', 0)
) AS demo (PublicId, Name, AllowsDirectBooking)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ClinicalServices AS s WHERE s.PublicId = demo.PublicId
);

-- Resolve public IDs to the internal IDs used in eligibility and referral rows.
DECLARE @AlexId int = (SELECT Id FROM dbo.Patients WHERE PublicId = @AlexPublicId);
DECLARE @JamieId int = (SELECT Id FROM dbo.Patients WHERE PublicId = @JamiePublicId);
DECLARE @RileyId int = (SELECT Id FROM dbo.Patients WHERE PublicId = @RileyPublicId);
DECLARE @BethId int = (SELECT Id FROM dbo.Providers WHERE PublicId = @BethPublicId);
DECLARE @JohnId int = (SELECT Id FROM dbo.Providers WHERE PublicId = @JohnPublicId);
DECLARE @ExamId int = (SELECT Id FROM dbo.ClinicalServices WHERE PublicId = @ExamPublicId);
DECLARE @TherapyId int = (SELECT Id FROM dbo.ClinicalServices WHERE PublicId = @TherapyPublicId);

-- Beth offers both services. John offers physical exams.
INSERT INTO dbo.ProviderServices (ProviderId, ClinicalServiceId, ClinicId)
SELECT demo.ProviderId, demo.ClinicalServiceId, @ClinicId
FROM (VALUES
    (@BethId, @ExamId),
    (@BethId, @TherapyId),
    (@JohnId, @ExamId)
) AS demo (ProviderId, ClinicalServiceId)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.ProviderServices AS link
    WHERE link.ProviderId = demo.ProviderId
      AND link.ClinicalServiceId = demo.ClinicalServiceId
);

-- Seed referrals for review, missing information, booking, and closed history.
-- Status values match ReferralStatus in the API.
DECLARE @PendingReview int = 1;
DECLARE @MissingInformation int = 2;
DECLARE @ReadyToSchedule int = 3;
DECLARE @Closed int = 4;
DECLARE @CreatedAtUtc datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

INSERT INTO dbo.Referrals (
    PublicId, PatientId, ClinicalServiceId, AssignedProviderId,
    ReferringProviderName, Reason, Status, MissingInformationNote, CreatedAtUtc
)
SELECT
    demo.PublicId, demo.PatientId, demo.ClinicalServiceId, demo.AssignedProviderId,
    N'Dr. Demo', demo.Reason, demo.Status, demo.Note, demo.CreatedAtUtc
FROM (VALUES
    (@ClosedReferralPublicId, @AlexId, @ExamId, @JohnId,
        N'Routine physical exam', @Closed, NULL, DATEADD(day, -7, @CreatedAtUtc)),
    (@PendingReferralPublicId, @JamieId, @TherapyId, @BethId,
        N'Therapy assessment', @PendingReview, NULL, @CreatedAtUtc),
    (@ReadyReferralPublicId, @AlexId, @TherapyId, @BethId,
        N'Follow-up therapy assessment', @ReadyToSchedule, NULL, @CreatedAtUtc),
    (@MissingInfoReferralPublicId, @JamieId, @ExamId, @JohnId,
        N'Follow-up physical exam', @MissingInformation, N'Please include the prior assessment.', @CreatedAtUtc),
    (@ScheduledReferralPublicId, @RileyId, @ExamId, @JohnId,
        N'Physical exam after referral', @ReadyToSchedule, NULL, @CreatedAtUtc)
) AS demo (PublicId, PatientId, ClinicalServiceId, AssignedProviderId, Reason, Status, Note, CreatedAtUtc)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Referrals AS r WHERE r.PublicId = demo.PublicId)
  AND (
      demo.Status = @Closed OR NOT EXISTS (
          SELECT 1 FROM dbo.Referrals AS r
          WHERE r.PatientId = demo.PatientId AND r.ClinicalServiceId = demo.ClinicalServiceId
            AND r.Status <> @Closed
      )
  );

-- Attribute the fictional booking to an account with no application privileges.
DECLARE @SeedIssuer nvarchar(255) = N'https://seed.invalid/';
DECLARE @SeedSubject nvarchar(255) = N'demo-booking-fixture';

IF NOT EXISTS (SELECT 1 FROM dbo.AppAccounts WHERE Issuer = @SeedIssuer AND Subject = @SeedSubject)
BEGIN
    INSERT INTO dbo.AppAccounts (Issuer, Subject, IsAdministrator)
    VALUES (@SeedIssuer, @SeedSubject, 0);
END;

DECLARE @SeedAccountId int = (
    SELECT Id FROM dbo.AppAccounts WHERE Issuer = @SeedIssuer AND Subject = @SeedSubject
);

-- Book the next weekday at 9 a.m. in Northside's time zone.
DECLARE @LocalTomorrow date = DATEADD(day, 1, CONVERT(date, @CreatedAtUtc AT TIME ZONE 'Central Standard Time'));
DECLARE @DayOfWeek int = DATEDIFF(day, DATEFROMPARTS(1900, 1, 1), @LocalTomorrow) % 7;
DECLARE @AppointmentDate date = DATEADD(day,
    CASE @DayOfWeek WHEN 5 THEN 2 WHEN 6 THEN 1 ELSE 0 END, @LocalTomorrow);
DECLARE @StartUtc datetimeoffset = SWITCHOFFSET(
    DATEADD(hour, 9, CAST(@AppointmentDate AS datetime2(0))) AT TIME ZONE 'Central Standard Time',
    '+00:00'
);
DECLARE @Confirmed int = 1;
DECLARE @Cancelled int = 2;

INSERT INTO dbo.Appointments (
    PublicId, PatientId, ClinicalServiceId, ProviderId, ReferralId,
    StartUtc, Status, BookedByAppAccountId, CreatedAtUtc
)
SELECT
    @AppointmentPublicId, r.PatientId, r.ClinicalServiceId, r.AssignedProviderId, r.Id,
    @StartUtc, @Confirmed, @SeedAccountId, @CreatedAtUtc
FROM dbo.Referrals AS r
WHERE r.PublicId = @ScheduledReferralPublicId
  AND r.Status = @ReadyToSchedule
  AND r.AssignedProviderId = @JohnId
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Appointments AS a
      WHERE a.PublicId = @AppointmentPublicId
         OR (a.ReferralId = r.Id AND a.Status <> @Cancelled)
         OR (a.Status = @Confirmed AND a.StartUtc = @StartUtc
             AND (a.PatientId = r.PatientId OR a.ProviderId = r.AssignedProviderId))
  );

COMMIT TRANSACTION;

PRINT N'Demo data ready: Northside, three patients, two providers, two services, and referral workflow examples.';
