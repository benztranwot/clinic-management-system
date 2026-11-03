-- Set the public ID for Northside Demo Clinic.
DECLARE @ClinicPublicId uniqueidentifier = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

-- Check whether a clinic with this public ID already exists.
IF NOT EXISTS (SELECT 1 FROM dbo.Clinics WHERE PublicId = @ClinicPublicId)
BEGIN
    -- Insert Northside Demo Clinic using the public ID defined above.
    INSERT INTO dbo.Clinics (PublicId, Name)
    VALUES (@ClinicPublicId, N'Northside Demo Clinic');
END;