-- Run in the same sqlcmd session immediately before SeedScaleData.sql.
EXEC sys.sp_set_session_context @key = N'SeedVisitorTemplate', @value = 1;
GO
