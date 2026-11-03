-- Run against the local HealthcareApp_Dev database after InitialCreate.
-- Inserts fictional fixtures, checks constraints, and rolls back all fixture rows.
-- Identity values can advance even though the inserted rows are rolled back.

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

-- Expected constraint failures should roll back their statement, not the fixtures.
SET XACT_ABORT OFF;

IF DB_NAME() <> N'HealthcareApp_Dev'
    THROW 51000, 'Run this script against HealthcareApp_Dev only.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ClinicA int, @ClinicB int, @Patient int;
    DECLARE @Beth int, @John int, @Taylor int, @Bob int;
    DECLARE @PhysicalExam int, @Referral int;
    DECLARE @Passed int = 0;
    DECLARE @Now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

    INSERT INTO dbo.Clinics (PublicId, Name)
    VALUES (NEWID(), N'Schema Check Clinic A');
    SET @ClinicA = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Clinics (PublicId, Name)
    VALUES (NEWID(), N'Schema Check Clinic B');
    SET @ClinicB = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Patients (PublicId, FirstName, LastName, DateOfBirth)
    VALUES (NEWID(), N'Alex', N'Schema Check', '2000-01-01');
    SET @Patient = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Providers (PublicId, ClinicId, FirstName, LastName)
    VALUES (NEWID(), @ClinicA, N'Beth', N'Schema Check');
    SET @Beth = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Providers (PublicId, ClinicId, FirstName, LastName)
    VALUES (NEWID(), @ClinicA, N'John', N'Schema Check');
    SET @John = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Providers (PublicId, ClinicId, FirstName, LastName)
    VALUES (NEWID(), @ClinicA, N'Taylor', N'Schema Check');
    SET @Taylor = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Providers (PublicId, ClinicId, FirstName, LastName)
    VALUES (NEWID(), @ClinicB, N'Bob', N'Schema Check');
    SET @Bob = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.ClinicalServices (PublicId, ClinicId, Name)
    VALUES (NEWID(), @ClinicA, N'Physical exam');
    SET @PhysicalExam = CONVERT(int, SCOPE_IDENTITY());

    -- Beth and John offer this service. Taylor has no eligibility entry.
    INSERT INTO dbo.ProviderServices (ProviderId, ClinicalServiceId, ClinicId)
    VALUES (@Beth, @PhysicalExam, @ClinicA), (@John, @PhysicalExam, @ClinicA);

    -- 1. A requested service can exist before provider assignment.
    INSERT INTO dbo.Referrals
        (PublicId, PatientId, ClinicalServiceId, AssignedProviderId,
         ReferringProviderName, Reason, Status, CreatedAtUtc)
    VALUES
        (NEWID(), @Patient, @PhysicalExam, NULL,
         N'Dr. Fictional Sender', N'Schema verification', 1, @Now);
    SET @Referral = CONVERT(int, SCOPE_IDENTITY());
    SET @Passed += 1;
    PRINT 'PASS 1: Unassigned referral accepted.';

    -- 2. Assign a provider who has the matching eligibility entry.
    UPDATE dbo.Referrals SET AssignedProviderId = @Beth WHERE Id = @Referral;
    SET @Passed += 1;
    PRINT 'PASS 2: Eligible provider assignment accepted.';

    -- 3. A different eligible provider must not bypass patient/service uniqueness.
    BEGIN TRY
        INSERT INTO dbo.Referrals
            (PublicId, PatientId, ClinicalServiceId, AssignedProviderId,
             ReferringProviderName, Reason, Status, CreatedAtUtc)
        VALUES
            (NEWID(), @Patient, @PhysicalExam, @John,
             N'Dr. Fictional Sender', N'Duplicate active request', 3, @Now);
        THROW 51000, 'FAIL 3: Duplicate active referral with another provider was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
        IF CHARINDEX(N'IX_Referrals_PatientId_ClinicalServiceId', ERROR_MESSAGE()) = 0 THROW;
        SET @Passed += 1;
        PRINT 'PASS 3: Duplicate active referral with another provider rejected.';
    END CATCH;

    -- 4. Leaving the new request unassigned must not bypass uniqueness either.
    BEGIN TRY
        INSERT INTO dbo.Referrals
            (PublicId, PatientId, ClinicalServiceId, AssignedProviderId,
             ReferringProviderName, Reason, Status, CreatedAtUtc)
        VALUES
            (NEWID(), @Patient, @PhysicalExam, NULL,
             N'Dr. Fictional Sender', N'Duplicate unassigned request', 1, @Now);
        THROW 51000, 'FAIL 4: Duplicate active unassigned referral was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
        IF CHARINDEX(N'IX_Referrals_PatientId_ClinicalServiceId', ERROR_MESSAGE()) = 0 THROW;
        SET @Passed += 1;
        PRINT 'PASS 4: Duplicate active unassigned referral rejected.';
    END CATCH;

    -- 5. Same-clinic membership alone does not establish service eligibility.
    BEGIN TRY
        UPDATE dbo.Referrals SET AssignedProviderId = @Taylor WHERE Id = @Referral;
        THROW 51000, 'FAIL 5: Ineligible provider assignment was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
        IF CHARINDEX(N'FK_Referrals_ProviderServices', ERROR_MESSAGE()) = 0 THROW;
        SET @Passed += 1;
        PRINT 'PASS 5: Provider without service eligibility rejected.';
    END CATCH;

    -- 6. A provider from clinic B cannot be linked to clinic A's service.
    BEGIN TRY
        INSERT INTO dbo.ProviderServices (ProviderId, ClinicalServiceId, ClinicId)
        VALUES (@Bob, @PhysicalExam, @ClinicA);
        THROW 51000, 'FAIL 6: Cross-clinic provider/service eligibility was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
        IF CHARINDEX(N'FK_ProviderServices_Providers', ERROR_MESSAGE()) = 0 THROW;
        SET @Passed += 1;
        PRINT 'PASS 6: Cross-clinic eligibility rejected.';
    END CATCH;

    -- 7. Values outside the declared status enum are not accepted by storage.
    BEGIN TRY
        UPDATE dbo.Referrals SET Status = 99 WHERE Id = @Referral;
        THROW 51000, 'FAIL 7: Undefined referral status was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
        IF CHARINDEX(N'CK_Referrals_Status', ERROR_MESSAGE()) = 0 THROW;
        SET @Passed += 1;
        PRINT 'PASS 7: Undefined status rejected.';
    END CATCH;

    -- 8. Closed history is excluded from active uniqueness.
    UPDATE dbo.Referrals SET Status = 4 WHERE Id = @Referral;

    INSERT INTO dbo.Referrals
        (PublicId, PatientId, ClinicalServiceId, AssignedProviderId,
         ReferringProviderName, Reason, Status, CreatedAtUtc)
    VALUES
        (NEWID(), @Patient, @PhysicalExam, @John,
         N'Dr. Fictional Sender', N'New request after closure', 1, @Now),
        (NEWID(), @Patient, @PhysicalExam, NULL,
         N'Dr. Fictional Sender', N'Additional closed history', 4, @Now);

    IF (SELECT COUNT(*) FROM dbo.Referrals
        WHERE PatientId = @Patient AND ClinicalServiceId = @PhysicalExam AND Status <> 4) <> 1
        THROW 51000, 'FAIL 8: Expected exactly one active referral after closure.', 1;

    IF (SELECT COUNT(*) FROM dbo.Referrals
        WHERE PatientId = @Patient AND ClinicalServiceId = @PhysicalExam AND Status = 4) <> 2
        THROW 51000, 'FAIL 8: Expected two preserved closed referrals.', 1;

    SET @Passed += 1;
    PRINT 'PASS 8: Closed history retained and new active referral accepted.';

    -- 9. Catalog entries cannot duplicate a name in the same clinic.
    BEGIN TRY
        INSERT INTO dbo.ClinicalServices (PublicId, ClinicId, Name)
        VALUES (NEWID(), @ClinicA, N'Physical exam');
        THROW 51000, 'FAIL 9: Duplicate service name in one clinic was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
        IF CHARINDEX(N'IX_ClinicalServices_ClinicId_Name', ERROR_MESSAGE()) = 0 THROW;
        SET @Passed += 1;
        PRINT 'PASS 9: Duplicate clinic service name rejected.';
    END CATCH;

    -- 10. A provider/service pair can be listed only once.
    BEGIN TRY
        INSERT INTO dbo.ProviderServices (ProviderId, ClinicalServiceId, ClinicId)
        VALUES (@John, @PhysicalExam, @ClinicA);
        THROW 51000, 'FAIL 10: Duplicate provider/service eligibility was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
        IF CHARINDEX(N'PK_ProviderServices', ERROR_MESSAGE()) = 0 THROW;
        SET @Passed += 1;
        PRINT 'PASS 10: Duplicate provider/service eligibility rejected.';
    END CATCH;

    IF XACT_STATE() <> 1 OR @Passed <> 10
        THROW 51000, 'Verification did not finish in the expected transaction state.', 1;

    ROLLBACK TRANSACTION;
    SELECT N'PASS' AS Result, @Passed AS ChecksPassed;
    PRINT 'All fictional fixture rows were rolled back.';
    PRINT 'These are sequential schema checks; concurrent-request behavior will be tested separately.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
