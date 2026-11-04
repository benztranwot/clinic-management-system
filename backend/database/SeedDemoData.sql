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
    THROW 51000, 'Run this seed against HealthcareApp_Dev or HealthcareApp_SeedCheck.', 1;

-- Keep the demo IDs stable so the same requests work after a reset.
DECLARE @ClinicPublicId uniqueidentifier = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
DECLARE @AlexPublicId uniqueidentifier = '11111111-1111-4111-8111-111111111111';
DECLARE @JamiePublicId uniqueidentifier = '99999999-9999-4999-8999-999999999999';
DECLARE @BethPublicId uniqueidentifier = '33333333-3333-4333-8333-333333333333';
DECLARE @JohnPublicId uniqueidentifier = '44444444-4444-4444-8444-444444444444';
DECLARE @ExamPublicId uniqueidentifier = '22222222-2222-4222-8222-222222222222';
DECLARE @TherapyPublicId uniqueidentifier = '55555555-5555-4555-8555-555555555555';
DECLARE @ClosedReferralPublicId uniqueidentifier = '77777777-7777-4777-8777-777777777777';
DECLARE @PendingReferralPublicId uniqueidentifier = '88888888-8888-4888-8888-888888888888';

-- Save all the seed rows together and roll back if an insert fails.
BEGIN TRANSACTION;

-- Insert Northside Demo Clinic if the demo clinic ID is missing.
IF NOT EXISTS (SELECT 1 FROM dbo.Clinics WHERE PublicId = @ClinicPublicId)
BEGIN
    INSERT INTO dbo.Clinics (PublicId, Name)
    VALUES (@ClinicPublicId, N'Northside Demo Clinic');
END;

-- Find the clinic's internal ID for the provider and service foreign keys.
DECLARE @ClinicId int = (SELECT Id FROM dbo.Clinics WHERE PublicId = @ClinicPublicId);

-- Insert the two fictional patients.
INSERT INTO dbo.Patients (PublicId, FirstName, LastName, DateOfBirth)
SELECT demo.PublicId, demo.FirstName, demo.LastName, demo.DateOfBirth
FROM (VALUES
    (@AlexPublicId, N'Alex', N'Demo', DATEFROMPARTS(2000, 1, 1)),
    (@JamiePublicId, N'Jamie', N'Demo', DATEFROMPARTS(1998, 4, 12))
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
INSERT INTO dbo.ClinicalServices (PublicId, ClinicId, Name)
SELECT demo.PublicId, @ClinicId, demo.Name
FROM (VALUES
    (@ExamPublicId, N'Physical exam'),
    (@TherapyPublicId, N'Therapy')
) AS demo (PublicId, Name)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ClinicalServices AS s WHERE s.PublicId = demo.PublicId
);

-- Resolve public IDs to the internal IDs used in eligibility and referral rows.
DECLARE @AlexId int = (SELECT Id FROM dbo.Patients WHERE PublicId = @AlexPublicId);
DECLARE @JamieId int = (SELECT Id FROM dbo.Patients WHERE PublicId = @JamiePublicId);
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

-- Insert one closed referral assigned to John and one unassigned pending referral.
-- Status values match ReferralStatus in the API.
DECLARE @PendingReview int = 1;
DECLARE @Closed int = 4;
DECLARE @CreatedAtUtc datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

INSERT INTO dbo.Referrals (
    PublicId, PatientId, ClinicalServiceId, AssignedProviderId,
    ReferringProviderName, Reason, Status, CreatedAtUtc
)
SELECT
    demo.PublicId, demo.PatientId, demo.ClinicalServiceId, demo.AssignedProviderId,
    N'Dr. Demo', demo.Reason, demo.Status, demo.CreatedAtUtc
FROM (VALUES
    (@ClosedReferralPublicId, @AlexId, @ExamId, @JohnId,
        N'Routine physical exam', @Closed, DATEADD(day, -7, @CreatedAtUtc)),
    (@PendingReferralPublicId, @JamieId, @TherapyId, NULL,
        N'Therapy assessment', @PendingReview, @CreatedAtUtc)
) AS demo (PublicId, PatientId, ClinicalServiceId, AssignedProviderId, Reason, Status, CreatedAtUtc)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Referrals AS r WHERE r.PublicId = demo.PublicId);

COMMIT TRANSACTION;

PRINT N'Demo data ready: Northside, Alex and Jamie, Beth and John, two services, three eligibility links, and two referrals.';
