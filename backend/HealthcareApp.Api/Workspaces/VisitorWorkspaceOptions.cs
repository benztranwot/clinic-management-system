namespace HealthcareApp.Api.Workspaces;

public sealed class VisitorWorkspaceOptions
{
    public bool Enabled { get; set; }
    public int MaximumWorkspaces { get; set; } = 100;
    public int MaximumDailyWorkspaces { get; set; } = 20;
}

public sealed class WorkspaceProvisioningException(int status, string code, string message, int? retryAfter = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public int? RetryAfter { get; } = retryAfter;
}
