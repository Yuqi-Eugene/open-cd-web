namespace OpenCd.Web.Models;

public enum JobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled
}

public sealed class JobInfo
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string CommandSummary { get; init; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? Error { get; set; }
    public string? WorkDir { get; init; }
    public string? LogFilePath { get; set; }
}

public sealed class JobLogSnapshot
{
    public required string JobId { get; init; }
    public required JobStatus Status { get; init; }
    public required IReadOnlyList<string> Lines { get; init; }
}
