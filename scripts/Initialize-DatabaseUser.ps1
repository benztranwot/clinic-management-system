[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')]
    [string]$ProjectName = 'clinic-management'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$savedOutputEncoding = $OutputEncoding
$passwordInput = $null
$plainPassword = $null
$escapedPassword = $null
$setupSql = $null

Push-Location $repoRoot
try {
    Write-Host 'Enter the same MSSQL_APP_PASSWORD saved in the root .env file.'
    $passwordInput = Read-Host 'Database application password' -AsSecureString
    $plainPassword = [Net.NetworkCredential]::new('', $passwordInput).Password

    if ($plainPassword.Length -lt 12 -or $plainPassword.Length -gt 128 -or
        $plainPassword.IndexOfAny([char[]]@([char]34, [char]39)) -ge 0 -or
        @($plainPassword.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0) {
        throw 'Use 12-128 characters without quotes or control characters, matching the root .env value.'
    }

    $escapedPassword = $plainPassword.Replace("'", "''")
    $setupSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE master;

IF DB_ID(N'HealthcareApp_Dev') IS NULL
    THROW 51000, 'Apply the development database migrations first.', 1;

IF OBJECT_ID(N'HealthcareApp_Dev.dbo.CreateVisitorWorkspaceAccount', N'P') IS NULL
    THROW 51000, 'Apply the visitor workspace migration first.', 1;

IF EXISTS (
    SELECT 1 FROM sys.server_role_members
    WHERE member_principal_id = SUSER_ID(N'clinic_app')
)
    THROW 51000, 'clinic_app already belongs to a server role. Review its privileges before continuing.', 1;

DECLARE @password nvarchar(128) = N'$escapedPassword';
DECLARE @command nvarchar(max);
IF SUSER_ID(N'clinic_app') IS NULL
    SET @command = N'CREATE LOGIN [clinic_app] WITH PASSWORD = N''' +
        REPLACE(@password, N'''', N'''''') + N''', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
ELSE
    SET @command = N'ALTER LOGIN [clinic_app] WITH PASSWORD = N''' +
        REPLACE(@password, N'''', N'''''') + N''', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
EXEC sys.sp_executesql @command;

USE HealthcareApp_Dev;
IF DATABASE_PRINCIPAL_ID(N'clinic_app') IS NULL
    CREATE USER [clinic_app] FOR LOGIN [clinic_app];
ELSE
    ALTER USER [clinic_app] WITH LOGIN = [clinic_app];

IF EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE member_principal_id = DATABASE_PRINCIPAL_ID(N'clinic_app')
)
    THROW 51000, 'clinic_app already belongs to a database role. Review its privileges before continuing.', 1;

GRANT CONNECT TO [clinic_app];
GRANT SELECT, INSERT ON OBJECT::dbo.Workspaces TO [clinic_app];
GRANT EXECUTE ON OBJECT::dbo.CreateVisitorWorkspaceAccount TO [clinic_app];
GRANT SELECT ON OBJECT::dbo.AppAccounts TO [clinic_app];
GRANT SELECT ON OBJECT::dbo.ClinicMemberships TO [clinic_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Clinics TO [clinic_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Patients TO [clinic_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Providers TO [clinic_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ClinicalServices TO [clinic_app];

GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.ProviderServices TO [clinic_app];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.ProviderWeeklyHours TO [clinic_app];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.ProviderTimeOff TO [clinic_app];

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Referrals TO [clinic_app];
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Appointments TO [clinic_app];

DECLARE @checks TABLE (
    LoginName nvarchar(128), IsAdministrator int,
    CanReadPatients int, CanUpdatePatients int,
    CanChangeAccounts int, CanCreateAccounts int, CanDeleteAccounts int,
    CanChangeMemberships int, CanCreateMemberships int, CanDeleteMemberships int,
    CanCreateTables int, CanCreateWorkspace int, CanEnrollVisitor int
);

EXECUTE AS LOGIN = N'clinic_app';
INSERT INTO @checks
SELECT SUSER_SNAME(), IS_SRVROLEMEMBER(N'sysadmin'),
    HAS_PERMS_BY_NAME(N'dbo.Patients', N'OBJECT', N'SELECT'),
    HAS_PERMS_BY_NAME(N'dbo.Patients', N'OBJECT', N'UPDATE'),
    HAS_PERMS_BY_NAME(N'dbo.AppAccounts', N'OBJECT', N'UPDATE'),
    HAS_PERMS_BY_NAME(N'dbo.AppAccounts', N'OBJECT', N'INSERT'),
    HAS_PERMS_BY_NAME(N'dbo.AppAccounts', N'OBJECT', N'DELETE'),
    HAS_PERMS_BY_NAME(N'dbo.ClinicMemberships', N'OBJECT', N'UPDATE'),
    HAS_PERMS_BY_NAME(N'dbo.ClinicMemberships', N'OBJECT', N'INSERT'),
    HAS_PERMS_BY_NAME(N'dbo.ClinicMemberships', N'OBJECT', N'DELETE'),
    HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE'),
    HAS_PERMS_BY_NAME(N'dbo.Workspaces', N'OBJECT', N'INSERT'),
    HAS_PERMS_BY_NAME(N'dbo.CreateVisitorWorkspaceAccount', N'OBJECT', N'EXECUTE');
REVERT;

SELECT * FROM @checks;
IF EXISTS (
    SELECT 1 FROM @checks
    WHERE ISNULL(IsAdministrator, -1) <> 0
       OR ISNULL(CanReadPatients, -1) <> 1 OR ISNULL(CanUpdatePatients, -1) <> 1
       OR ISNULL(CanChangeAccounts, -1) <> 0 OR ISNULL(CanCreateAccounts, -1) <> 0
       OR ISNULL(CanDeleteAccounts, -1) <> 0 OR ISNULL(CanChangeMemberships, -1) <> 0
       OR ISNULL(CanCreateMemberships, -1) <> 0 OR ISNULL(CanDeleteMemberships, -1) <> 0
       OR ISNULL(CanCreateTables, -1) <> 0
       OR ISNULL(CanCreateWorkspace, -1) <> 1 OR ISNULL(CanEnrollVisitor, -1) <> 1
)
    THROW 51000, 'The database user has unexpected effective permissions. Review the displayed checks.', 1;
"@

    # Send SQL through standard input; keep passwords out of files and command arguments.
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    $setupSql | docker compose -p $ProjectName exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -x -d master'
    if ($LASTEXITCODE -ne 0) {
        throw 'Database-user setup failed. Check the SQL error above.'
    }

    Write-Host 'Database user configured. The API password must match MSSQL_APP_PASSWORD in .env.'
}
finally {
    $OutputEncoding = $savedOutputEncoding
    $setupSql = $null
    $escapedPassword = $null
    $plainPassword = $null
    if ($null -ne $passwordInput) { $passwordInput.Dispose() }
    Pop-Location
}
