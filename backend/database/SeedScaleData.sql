-- Add the larger fictional dataset to the demo database or an isolated automated test.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET DATEFIRST 1;

DECLARE @IsIntegrationTest bit = CASE
    WHEN LEFT(DB_NAME(), 31) = N'HealthcareApp_IntegrationTests_' AND LEN(DB_NAME()) = 63 THEN 1 ELSE 0 END;

IF DB_NAME() NOT IN (N'HealthcareApp_Dev', N'HealthcareApp_SeedCheck') AND @IsIntegrationTest = 0
    THROW 51000, 'Run this seed against the demo database or an integration-test database.', 1;

-- The normal seed stays in the original workspace. The privileged template command
-- selects a reserved workspace and a separate public-ID range in this session.
DECLARE @TemplateMode bit = CASE WHEN SESSION_CONTEXT(N'SeedVisitorTemplate') = 1 THEN 1 ELSE 0 END;
DECLARE @WorkspaceId uniqueidentifier = CASE WHEN @TemplateMode = 1
    THEN '00000000-0000-0000-0000-000000000001' ELSE '00000000-0000-0000-0000-000000000000' END;
DECLARE @IdPrefix char(2) = CASE WHEN @TemplateMode = 1 THEN '56' ELSE '55' END;
DECLARE @FixtureSubject nvarchar(255) = CASE WHEN @TemplateMode = 1
    THEN N'visitor-template-booking-fixture' ELSE N'demo-booking-fixture' END;

BEGIN TRANSACTION;

DECLARE @LockResult int;
EXEC @LockResult = sys.sp_getapplock
    @Resource = N'HealthcareApp-ScaleSeed', @LockMode = 'Exclusive',
    @LockOwner = 'Transaction', @LockTimeout = 10000;
IF @LockResult < 0
    THROW 51001, 'Another scale seed is running. Retry after it finishes.', 1;

IF @TemplateMode = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = @WorkspaceId)
    INSERT INTO dbo.Workspaces (Id, CreatedAtUtc)
    VALUES (@WorkspaceId, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));

-- Stable public IDs let reruns add missing records without overwriting existing data.
DECLARE @NowUtc datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
DECLARE @Today date = CONVERT(date, @NowUtc AT TIME ZONE 'Central Standard Time');

SELECT 1 + ones.N + 10 * tens.N + 100 * hundreds.N AS N
INTO #Numbers
FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) ones(N)
CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) tens(N)
CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) hundreds(N);

SELECT ROW_NUMBER() OVER (ORDER BY N) - 1 AS DayNumber, DATEADD(day, N, @Today) AS LocalDate
INTO #Days
FROM #Numbers
WHERE N <= 14 AND DATEPART(weekday, DATEADD(day, N, @Today)) <= 5;

-- Synthetic names and service descriptions used throughout the visible records.
SELECT names.N, names.Name, names.Address INTO #ClinicNames
FROM (VALUES
    (1, N'Maple Grove Family Health', N'1420 Willow Lane'),
    (2, N'Riverbend Rehabilitation', N'680 Brookside Avenue'),
    (3, N'Cedar Ridge Cardiology', N'235 Cedar Ridge Road'),
    (4, N'Lakeshore Orthopedic Care', N'910 Harbor View Drive'),
    (5, N'Oak Valley Behavioral Health', N'475 Oak Valley Parkway')
) names(N, Name, Address);

SELECT names.N, names.FirstName, names.LastName INTO #ProviderNames
FROM (VALUES
    (1, N'Maya', N'Patel'),
    (2, N'Daniel', N'Brooks'),
    (3, N'Elena', N'Ramirez'),
    (4, N'Samuel', N'Chen'),
    (5, N'Nora', N'Thompson'),
    (6, N'Olivia', N'Bennett'),
    (7, N'Ethan', N'Nguyen'),
    (8, N'Sofia', N'Martinez'),
    (9, N'Caleb', N'Anderson'),
    (10, N'Priya', N'Shah'),
    (11, N'Adrian', N'Collins'),
    (12, N'Grace', N'Kim'),
    (13, N'Lucas', N'Rivera'),
    (14, N'Hannah', N'Wilson'),
    (15, N'Aiden', N'Morgan'),
    (16, N'Isabella', N'Turner'),
    (17, N'Marcus', N'Reed'),
    (18, N'Chloe', N'Park'),
    (19, N'Gabriel', N'Torres'),
    (20, N'Natalie', N'Foster'),
    (21, N'Leila', N'Hassan'),
    (22, N'Owen', N'Mitchell'),
    (23, N'Amara', N'Okafor'),
    (24, N'Julian', N'Scott'),
    (25, N'Vivian', N'Lee')
) names(N, FirstName, LastName);

SELECT n.N, firstNames.FirstName, lastNames.LastName INTO #PatientNames
FROM #Numbers n
JOIN (VALUES
    (1, N'Emma'),
    (2, N'Liam'),
    (3, N'Ava'),
    (4, N'Noah'),
    (5, N'Charlotte'),
    (6, N'Oliver'),
    (7, N'Amelia'),
    (8, N'Elijah'),
    (9, N'Sophia'),
    (10, N'James'),
    (11, N'Isabella'),
    (12, N'Henry'),
    (13, N'Mia'),
    (14, N'Benjamin'),
    (15, N'Harper'),
    (16, N'Lucas'),
    (17, N'Evelyn'),
    (18, N'Alexander'),
    (19, N'Abigail'),
    (20, N'Ethan'),
    (21, N'Emily'),
    (22, N'Daniel'),
    (23, N'Elizabeth'),
    (24, N'Michael'),
    (25, N'Sofia')
) firstNames(N, FirstName) ON firstNames.N = (n.N - 1) % 25 + 1
JOIN (VALUES
    (1, N'Carter'),
    (2, N'Mitchell'),
    (3, N'Sullivan'),
    (4, N'Bennett'),
    (5, N'Walker'),
    (6, N'Reynolds'),
    (7, N'Nguyen'),
    (8, N'Patel'),
    (9, N'Ramirez'),
    (10, N'Thompson')
) lastNames(N, LastName) ON lastNames.N = (n.N - 1) / 25 + 1
WHERE n.N <= 250;

SELECT names.N, names.Name, names.Reason, names.MissingNote INTO #ServiceNames
FROM (VALUES
    (1, N'Primary Care Consultation', N'Evaluate persistent fatigue and arrange primary care follow-up.', N'Please attach the recent primary care visit summary.'),
    (2, N'Annual Physical Exam', N'Annual health assessment and preventive care review.', N'Please include the current medication list and prior health assessment.'),
    (3, N'Blood Pressure Review', N'Review home blood-pressure readings and current treatment.', N'Please attach the recent home blood-pressure log.'),
    (4, N'Diabetes Follow-up', N'Review glucose records and ongoing diabetes care.', N'Please attach the recent glucose log and laboratory results.'),
    (5, N'Physical Therapy Evaluation', N'Assess reduced mobility following a recent ankle injury.', N'Please attach the referring clinician''s assessment and activity restrictions.'),
    (6, N'Back Pain Rehabilitation', N'Assess persistent lower back discomfort and functional limitations.', N'Please include prior treatment notes and available imaging reports.'),
    (7, N'Postoperative Rehabilitation', N'Plan rehabilitation following recent joint surgery.', N'Please attach the operative report and postoperative rehabilitation protocol.'),
    (8, N'Balance and Mobility Assessment', N'Evaluate recurrent imbalance and difficulty walking on uneven surfaces.', N'Please include the recent mobility assessment and relevant treatment history.'),
    (9, N'Cardiology Consultation', N'Evaluate intermittent palpitations and review cardiovascular history.', N'Please attach the referral summary and available cardiac test results.'),
    (10, N'ECG Review', N'Review a recent ECG and discuss further follow-up.', N'Please attach the ECG tracing and the referring clinician''s interpretation.'),
    (11, N'Heart Failure Follow-up', N'Follow-up review after a recent heart failure admission.', N'Please attach the hospital discharge summary and current medication list.'),
    (12, N'Cholesterol Management', N'Review lipid results and ongoing cholesterol management.', N'Please attach the latest lipid panel and relevant medication history.'),
    (13, N'Orthopedic Consultation', N'Evaluate persistent joint discomfort affecting daily activities.', N'Please include prior treatment notes and available imaging reports.'),
    (14, N'Knee Pain Assessment', N'Assess knee discomfort and reduced range of motion.', N'Please attach the recent knee examination and available imaging reports.'),
    (15, N'Shoulder Injury Review', N'Review shoulder pain and weakness following an injury.', N'Please include the injury assessment and prior treatment notes.'),
    (16, N'Fracture Follow-up', N'Assess recovery and mobility following fracture treatment.', N'Please attach the initial fracture report and follow-up imaging.'),
    (17, N'Mental Health Assessment', N'Assess recent mood changes and their impact on daily functioning.', N'Please include the referral summary and relevant treatment history.'),
    (18, N'Anxiety Follow-up', N'Review ongoing anxiety symptoms and response to the current care plan.', N'Please attach the recent progress notes and current medication list.'),
    (19, N'Depression Follow-up', N'Review mood symptoms and progress with the current care plan.', N'Please include the recent assessment and treatment summary.'),
    (20, N'Medication Management', N'Review medication response and discuss reported side effects.', N'Please attach the current medication list and recent treatment notes.')
) names(N, Name, Reason, MissingNote);

-- Upgrade earlier numbered seed labels while preserving names edited by a visitor.
UPDATE c SET
    Name = CASE WHEN c.Name IN (CONCAT(N'Demo Clinic ', names.N), CONCAT(N'Scale Clinic ', names.N))
        THEN names.Name ELSE c.Name END,
    Address = CASE WHEN c.Address = CONCAT(names.N, N' Fictional Avenue') THEN names.Address ELSE c.Address END
FROM dbo.Clinics c JOIN #ClinicNames names ON c.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000001-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12)))
WHERE c.WorkspaceId = @WorkspaceId AND (c.Name IN (CONCAT(N'Demo Clinic ', names.N), CONCAT(N'Scale Clinic ', names.N))
    OR c.Address = CONCAT(names.N, N' Fictional Avenue'));

INSERT INTO dbo.Clinics (WorkspaceId, PublicId, Name, Address, TimeZoneId)
SELECT @WorkspaceId, CONVERT(uniqueidentifier, CONCAT(@IdPrefix, '000001-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))),
    names.Name, names.Address, N'America/Chicago'
FROM #ClinicNames names
WHERE NOT EXISTS (SELECT 1 FROM dbo.Clinics c WHERE c.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000001-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))));

UPDATE p SET FirstName = names.FirstName, LastName = names.LastName
FROM dbo.Patients p JOIN #PatientNames names ON p.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000004-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12)))
WHERE p.WorkspaceId = @WorkspaceId AND p.FirstName = N'Fictional' AND p.LastName = CONCAT(N'Patient ', RIGHT(CONCAT('000', names.N), 3));

INSERT INTO dbo.Patients (WorkspaceId, PublicId, FirstName, LastName, DateOfBirth)
SELECT @WorkspaceId, CONVERT(uniqueidentifier, CONCAT(@IdPrefix, '000004-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))),
    names.FirstName, names.LastName, DATEFROMPARTS(1950 + names.N * 7 % 55, names.N * 5 % 12 + 1, names.N * 11 % 28 + 1)
FROM #PatientNames names
WHERE NOT EXISTS (SELECT 1 FROM dbo.Patients p WHERE p.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000004-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))));

UPDATE p SET FirstName = names.FirstName, LastName = names.LastName
FROM dbo.Providers p JOIN #ProviderNames names ON p.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000002-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12)))
WHERE p.WorkspaceId = @WorkspaceId AND p.FirstName = N'Fictional' AND p.LastName = CONCAT(N'Provider ', RIGHT(CONCAT('00', names.N), 2));

INSERT INTO dbo.Providers (WorkspaceId, PublicId, ClinicId, FirstName, LastName)
SELECT @WorkspaceId, CONVERT(uniqueidentifier, CONCAT(@IdPrefix, '000002-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))),
    c.Id, names.FirstName, names.LastName
FROM #ProviderNames names
JOIN dbo.Clinics c ON c.WorkspaceId = @WorkspaceId AND c.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000001-0000-4000-8000-', RIGHT(CONCAT('000000000000', (names.N - 1) / 5 + 1), 12)))
WHERE NOT EXISTS (SELECT 1 FROM dbo.Providers p WHERE p.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000002-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))));

UPDATE s SET Name = names.Name
FROM dbo.ClinicalServices s JOIN #ServiceNames names ON s.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000003-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12)))
WHERE s.WorkspaceId = @WorkspaceId AND s.Name IN (CONCAT(N'Demo Service ', RIGHT(CONCAT('00', names.N), 2)),
    CONCAT(N'Scale Service ', RIGHT(CONCAT('00', names.N), 2)))
    AND NOT EXISTS (SELECT 1 FROM dbo.ClinicalServices other
        WHERE other.ClinicId = s.ClinicId AND other.Name = names.Name AND other.Id <> s.Id);

INSERT INTO dbo.ClinicalServices (WorkspaceId, PublicId, ClinicId, Name, AllowsDirectBooking)
SELECT @WorkspaceId, CONVERT(uniqueidentifier, CONCAT(@IdPrefix, '000003-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))),
    c.Id, names.Name, 1
FROM #ServiceNames names
JOIN dbo.Clinics c ON c.WorkspaceId = @WorkspaceId AND c.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000001-0000-4000-8000-', RIGHT(CONCAT('000000000000', (names.N - 1) / 4 + 1), 12)))
WHERE NOT EXISTS (SELECT 1 FROM dbo.ClinicalServices s WHERE s.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000003-0000-4000-8000-', RIGHT(CONCAT('000000000000', names.N), 12))));

INSERT INTO dbo.ProviderServices (WorkspaceId, ProviderId, ClinicalServiceId, ClinicId)
SELECT @WorkspaceId, p.Id, s.Id, p.ClinicId
FROM dbo.Providers p JOIN dbo.ClinicalServices s ON s.ClinicId = p.ClinicId AND s.WorkspaceId = @WorkspaceId
WHERE p.WorkspaceId = @WorkspaceId AND CONVERT(char(36), p.PublicId) LIKE CONCAT(@IdPrefix, '000002-0000-4000-8000-%')
    AND CONVERT(char(36), s.PublicId) LIKE CONCAT(@IdPrefix, '000003-0000-4000-8000-%')
    AND NOT EXISTS (SELECT 1 FROM dbo.ProviderServices link
        WHERE link.ProviderId = p.Id AND link.ClinicalServiceId = s.Id);

DECLARE @BookingAccountId int;
IF @IsIntegrationTest = 1 AND @TemplateMode = 0
BEGIN
    INSERT INTO dbo.AppAccounts (Issuer, Subject, IsAdministrator, ProviderId)
    SELECT N'https://auth.integration.test/', demo.Subject, demo.IsAdministrator, demo.ProviderId
    FROM (VALUES
        (N'auth0|scale-admin', 1, CAST(NULL AS int)),
        (N'auth0|scale-front-desk', 0, NULL),
        (N'auth0|scale-provider', 0,
            (SELECT Id FROM dbo.Providers WHERE PublicId = '55000002-0000-4000-8000-000000000001'))
    ) demo(Subject, IsAdministrator, ProviderId)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AppAccounts a
        WHERE a.Issuer = N'https://auth.integration.test/' AND a.Subject = demo.Subject);

    INSERT INTO dbo.ClinicMemberships (AppAccountId, ClinicId, Role)
    SELECT a.Id, c.Id, 2 FROM dbo.AppAccounts a CROSS JOIN dbo.Clinics c
    WHERE a.Issuer = N'https://auth.integration.test/' AND a.Subject = N'auth0|scale-front-desk'
        AND CONVERT(char(36), c.PublicId) LIKE CONCAT(@IdPrefix, '000001-0000-4000-8000-%')
        AND NOT EXISTS (SELECT 1 FROM dbo.ClinicMemberships m
            WHERE m.AppAccountId = a.Id AND m.ClinicId = c.Id AND m.Role = 2);
    INSERT INTO dbo.ClinicMemberships (AppAccountId, ClinicId, Role)
    SELECT a.Id, p.ClinicId, 1 FROM dbo.AppAccounts a JOIN dbo.Providers p ON p.Id = a.ProviderId
    WHERE a.Issuer = N'https://auth.integration.test/' AND a.Subject = N'auth0|scale-provider'
        AND NOT EXISTS (SELECT 1 FROM dbo.ClinicMemberships m
            WHERE m.AppAccountId = a.Id AND m.ClinicId = p.ClinicId AND m.Role = 1);

    SELECT @BookingAccountId = Id FROM dbo.AppAccounts
    WHERE Issuer = N'https://auth.integration.test/' AND Subject = N'auth0|scale-admin';
END
ELSE
BEGIN
    -- Attribute fictional demo bookings to the existing unprivileged seed identity.
    IF NOT EXISTS (SELECT 1 FROM dbo.AppAccounts
        WHERE Issuer = N'https://seed.invalid/' AND Subject = @FixtureSubject)
        INSERT INTO dbo.AppAccounts (WorkspaceId, Issuer, Subject, IsAdministrator)
        VALUES (@WorkspaceId, N'https://seed.invalid/', @FixtureSubject, 0);

    SELECT @BookingAccountId = Id FROM dbo.AppAccounts
    WHERE WorkspaceId = @WorkspaceId AND Issuer = N'https://seed.invalid/' AND Subject = @FixtureSubject;
    IF @BookingAccountId IS NULL
        THROW 51002, 'The booking fixture belongs to a different workspace.', 1;
END;

-- Each provider has four morning visits per weekday for ten weekdays.
-- Patient numbers are different within every time slot, preserving both unique slot constraints.
SELECT n.N, p.Id AS ProviderId, patient.Id AS PatientId, service.Id AS ClinicalServiceId,
    ((n.N - 1) % 25) / 5 * 4 + ((n.N - 1) / 25) % 4 + 1 AS ServiceNumber,
    CASE n.N % 5 WHEN 0 THEN N'Dr. Amelia Warren' WHEN 1 THEN N'Dr. Benjamin Ellis'
        WHEN 2 THEN N'Dr. Sophie Grant' WHEN 3 THEN N'Dr. Nathan Price' ELSE N'Dr. Aisha Rahman' END AS ReferringProviderName,
    CASE WHEN n.N <= 120 THEN CASE WHEN n.N % 2 = 0 THEN 3 ELSE 1 END
        WHEN n.N % 10 = 0 THEN 2 WHEN n.N % 10 = 1 THEN 3 ELSE 1 END AS AppointmentStatus,
    DATEADD(minute, 480 + ((n.N - 1) / 25) % 4 * 30, CONVERT(datetime2, d.LocalDate)) AS LocalStart
INTO #Bookings
FROM #Numbers n
JOIN #Days d ON d.DayNumber = (n.N - 1) / 100
JOIN dbo.Providers p ON p.WorkspaceId = @WorkspaceId AND p.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000002-0000-4000-8000-', RIGHT(CONCAT('000000000000', (n.N - 1) % 25 + 1), 12)))
JOIN dbo.Patients patient ON patient.WorkspaceId = @WorkspaceId AND patient.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000004-0000-4000-8000-', RIGHT(CONCAT('000000000000', (n.N - 1) % 250 + 1), 12)))
JOIN dbo.ClinicalServices service ON service.WorkspaceId = @WorkspaceId AND service.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000003-0000-4000-8000-', RIGHT(CONCAT('000000000000',
        ((n.N - 1) % 25) / 5 * 4 + ((n.N - 1) / 25) % 4 + 1), 12)));

-- Update only the original placeholder text on previously seeded referrals.
UPDATE r SET
    ReferringProviderName = CASE WHEN r.ReferringProviderName = N'Fictional Referring Provider'
        THEN b.ReferringProviderName ELSE r.ReferringProviderName END,
    Reason = CASE WHEN r.Reason IN (CONCAT(N'Fictional demo referral ', b.N), CONCAT(N'Fictional scale referral ', b.N))
        THEN names.Reason ELSE r.Reason END,
    MissingInformationNote = CASE WHEN r.MissingInformationNote = N'Fictional supporting information needed.'
        THEN names.MissingNote ELSE r.MissingInformationNote END
FROM dbo.Referrals r JOIN #Bookings b ON b.N <= 300 AND r.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000005-0000-4000-8000-', RIGHT(CONCAT('000000000000', b.N), 12)))
JOIN #ServiceNames names ON names.N = b.ServiceNumber
WHERE r.WorkspaceId = @WorkspaceId AND r.ClinicalServiceId = b.ClinicalServiceId
    AND (r.ReferringProviderName = N'Fictional Referring Provider'
        OR r.Reason IN (CONCAT(N'Fictional demo referral ', b.N), CONCAT(N'Fictional scale referral ', b.N))
        OR r.MissingInformationNote = N'Fictional supporting information needed.');

-- Sixty referrals per stage: pending review, missing information, ready, scheduled, and closed.
INSERT INTO dbo.Referrals
    (WorkspaceId, PublicId, PatientId, ClinicalServiceId, AssignedProviderId, ReferringProviderName,
        Reason, MissingInformationNote, Status, CreatedAtUtc)
SELECT @WorkspaceId, CONVERT(uniqueidentifier, CONCAT(@IdPrefix, '000005-0000-4000-8000-', RIGHT(CONCAT('000000000000', b.N), 12))),
    b.PatientId, b.ClinicalServiceId, b.ProviderId, b.ReferringProviderName, names.Reason,
    CASE WHEN b.N > 120 AND b.N % 3 = 1 THEN names.MissingNote END,
    CASE WHEN b.N <= 120 THEN CASE WHEN b.N % 2 = 0 THEN 4 ELSE 3 END ELSE b.N % 3 + 1 END,
    DATEADD(minute, b.N - 301, DATEADD(day, -61, @NowUtc))
FROM #Bookings b JOIN #ServiceNames names ON names.N = b.ServiceNumber WHERE b.N <= 300
    AND NOT EXISTS (SELECT 1 FROM dbo.Referrals r WHERE r.PublicId = CONVERT(uniqueidentifier,
        CONCAT(@IdPrefix, '000005-0000-4000-8000-', RIGHT(CONCAT('000000000000', b.N), 12))))
    AND (b.N <= 120 AND b.N % 2 = 0 OR NOT EXISTS (SELECT 1 FROM dbo.Referrals r
        WHERE r.PatientId = b.PatientId AND r.ClinicalServiceId = b.ClinicalServiceId AND r.Status <> 4));

INSERT INTO dbo.Appointments
    (WorkspaceId, PublicId, PatientId, ClinicalServiceId, ProviderId, ReferralId, StartUtc,
        Status, BookedByAppAccountId, CreatedAtUtc)
SELECT @WorkspaceId, CONVERT(uniqueidentifier, CONCAT(@IdPrefix, '000006-0000-4000-8000-', RIGHT(CONCAT('000000000000', b.N), 12))),
    b.PatientId, b.ClinicalServiceId, b.ProviderId, r.Id,
    CONVERT(datetimeoffset, DATEADD(day, CASE WHEN b.AppointmentStatus = 3 THEN -28 ELSE 0 END, b.LocalStart)
        AT TIME ZONE 'Central Standard Time' AT TIME ZONE 'UTC'),
    b.AppointmentStatus, @BookingAccountId, DATEADD(day, -60, @NowUtc)
FROM #Bookings b
LEFT JOIN dbo.Referrals r ON r.WorkspaceId = @WorkspaceId AND b.N <= 120 AND r.PublicId = CONVERT(uniqueidentifier,
    CONCAT(@IdPrefix, '000005-0000-4000-8000-', RIGHT(CONCAT('000000000000', b.N), 12)))
    AND r.PatientId = b.PatientId AND r.ClinicalServiceId = b.ClinicalServiceId
    AND r.AssignedProviderId = b.ProviderId
WHERE NOT EXISTS (SELECT 1 FROM dbo.Appointments a WHERE a.PublicId = CONVERT(uniqueidentifier,
        CONCAT(@IdPrefix, '000006-0000-4000-8000-', RIGHT(CONCAT('000000000000', b.N), 12))))
    AND (b.N > 120 OR r.Id IS NOT NULL)
    AND (r.Id IS NULL OR r.Status = CASE WHEN b.AppointmentStatus = 3 THEN 4 ELSE 3 END)
    AND (r.Id IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Appointments a WHERE a.ReferralId = r.Id AND a.Status <> 2))
    AND (b.AppointmentStatus <> 1 OR NOT EXISTS (
        SELECT 1 FROM dbo.Appointments a
        WHERE a.Status = 1 AND (a.PatientId = b.PatientId OR a.ProviderId = b.ProviderId)
            AND a.StartUtc = CONVERT(datetimeoffset, b.LocalStart AT TIME ZONE 'Central Standard Time' AT TIME ZONE 'UTC')));

INSERT INTO dbo.ProviderTimeOff (WorkspaceId, PublicId, ProviderId, StartUtc, EndUtc)
SELECT @WorkspaceId, CONVERT(uniqueidentifier, CONCAT(@IdPrefix, '000008-0000-4000-8000-', RIGHT(CONCAT('000000000000', N), 12))),
    ProviderId, CONVERT(datetimeoffset, LocalStart AT TIME ZONE 'Central Standard Time' AT TIME ZONE 'UTC'),
    CONVERT(datetimeoffset, DATEADD(minute, 60, LocalStart) AT TIME ZONE 'Central Standard Time' AT TIME ZONE 'UTC')
FROM #Bookings b WHERE N <= 25 AND (N - 1) % 5 = 0
    AND NOT EXISTS (SELECT 1 FROM dbo.ProviderTimeOff t WHERE t.PublicId = CONVERT(uniqueidentifier,
        CONCAT(@IdPrefix, '000008-0000-4000-8000-', RIGHT(CONCAT('000000000000', b.N), 12))));

DROP TABLE #ClinicNames;
DROP TABLE #ProviderNames;
DROP TABLE #PatientNames;
DROP TABLE #ServiceNames;
DROP TABLE #Bookings;
DROP TABLE #Days;
DROP TABLE #Numbers;
COMMIT;

EXEC sys.sp_set_session_context @key = N'SeedVisitorTemplate', @value = NULL;

PRINT N'Larger fictional demo dataset ready. Existing records and account links were preserved.';
SELECT
    (SELECT COUNT(*) FROM dbo.Clinics WHERE WorkspaceId = @WorkspaceId) AS Clinics,
    (SELECT COUNT(*) FROM dbo.Providers WHERE WorkspaceId = @WorkspaceId) AS Providers,
    (SELECT COUNT(*) FROM dbo.ClinicalServices WHERE WorkspaceId = @WorkspaceId) AS Services,
    (SELECT COUNT(*) FROM dbo.Patients WHERE WorkspaceId = @WorkspaceId) AS Patients,
    (SELECT COUNT(*) FROM dbo.Referrals WHERE WorkspaceId = @WorkspaceId) AS Referrals,
    (SELECT COUNT(*) FROM dbo.Appointments WHERE WorkspaceId = @WorkspaceId) AS Appointments;
