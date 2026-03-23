using System.Collections.Concurrent;
using System.Diagnostics;
using OpenCd.Web.Models;

namespace OpenCd.Web.Services;

public sealed class JobRunnerService
{
    private sealed class RuntimeState
    {
        public required JobInfo Job { get; init; }
        public ConcurrentQueue<string> Lines { get; } = new();
        public int ProcessId { get; set; }
        public DateTimeOffset LastProcessOutputAt { get; set; } = DateTimeOffset.UtcNow;
        public object WriterLock { get; } = new();
    }

    private readonly ConcurrentDictionary<string, RuntimeState> _jobs = new();
    private readonly PathService _pathService;
    private readonly ILogger<JobRunnerService> _logger;

    public JobRunnerService(
        PathService pathService,
        ILogger<JobRunnerService> logger,
        IHostApplicationLifetime lifetime)
    {
        _pathService = pathService;
        _logger = logger;
        lifetime.ApplicationStopping.Register(() => _ = CancelAllRunningJobs());
    }

    public IReadOnlyCollection<JobInfo> ListJobs()
    {
        return _jobs.Values
            .Select(x => x.Job)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public JobInfo? GetJob(string id)
    {
        return _jobs.TryGetValue(id, out var state) ? state.Job : null;
    }

    public JobLogSnapshot? GetLog(string id, int tail = 300)
    {
        if (!_jobs.TryGetValue(id, out var state))
        {
            return null;
        }

        var lines = state.Lines.ToArray();
        if (tail > 0 && lines.Length > tail)
        {
            lines = lines[^tail..];
        }

        return new JobLogSnapshot
        {
            JobId = id,
            Status = state.Job.Status,
            Lines = lines
        };
    }

    public JobInfo StartJob(
        string type,
        string fileName,
        IEnumerable<string> args,
        string? workDir = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var argList = args.ToList();
        var id = Guid.NewGuid().ToString("N");
        var safeWorkDir = string.IsNullOrWhiteSpace(workDir)
            ? _pathService.RepoRoot
            : _pathService.ResolveInsideRepo(workDir);

        var job = new JobInfo
        {
            Id = id,
            Type = type,
            CommandSummary = $"{fileName} {string.Join(' ', argList)}",
            WorkDir = _pathService.ToRepoRelative(safeWorkDir)
        };

        var runtime = new RuntimeState { Job = job };
        if (!_jobs.TryAdd(id, runtime))
        {
            throw new InvalidOperationException("Unable to register job.");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                job.Status = JobStatus.Running;
                job.StartedAt = DateTimeOffset.UtcNow;

                var logsDir = Path.Combine(_pathService.RepoRoot, "runtime", "jobs");
                Directory.CreateDirectory(logsDir);
                var logFile = Path.Combine(logsDir, $"{id}.log");
                job.LogFilePath = _pathService.ToRepoRelative(logFile);

                using var writer = new StreamWriter(logFile, append: false);
                using var heartbeatCts = new CancellationTokenSource();

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    WorkingDirectory = safeWorkDir,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var arg in argList)
                {
                    psi.ArgumentList.Add(arg);
                }

                if (environment is not null)
                {
                    foreach (var kv in environment)
                    {
                        psi.Environment[kv.Key] = kv.Value;
                    }
                }

                OnLine(runtime, writer, $"[CMD] {fileName}");
                OnLine(runtime, writer, $"[CWD] {safeWorkDir}");
                if (argList.Count > 0)
                {
                    OnLine(runtime, writer, $"[ARGS] {string.Join(' ', argList)}");
                }
                if (environment is not null && environment.Count > 0)
                {
                    OnLine(runtime, writer, $"[ENV] {string.Join(", ", environment.Select(kv => $"{kv.Key}={kv.Value}"))}");
                }

                var heartbeatTask = Task.Run(async () =>
                {
                    while (!heartbeatCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1), heartbeatCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        var silence = DateTimeOffset.UtcNow - runtime.LastProcessOutputAt;
                        var silenceText = silence < TimeSpan.Zero ? "0s" : $"{(int)silence.TotalMinutes}m {silence.Seconds}s";
                        OnLine(runtime, writer, $"[HEARTBEAT] 任务仍在运行，距离上次训练输出 {silenceText}。", countAsProcessOutput: false);
                    }
                }, heartbeatCts.Token);

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => OnLine(runtime, writer, e.Data);
                process.ErrorDataReceived += (_, e) => OnLine(runtime, writer, e.Data);

                process.Start();
                runtime.ProcessId = process.Id;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                heartbeatCts.Cancel();
                await heartbeatTask;
                OnLine(runtime, writer, $"[PROCESS] exited with code {process.ExitCode}", countAsProcessOutput: false);

                job.ExitCode = process.ExitCode;
                job.EndedAt = DateTimeOffset.UtcNow;
                job.Status = process.ExitCode == 0 ? JobStatus.Succeeded : JobStatus.Failed;

                if (process.ExitCode != 0)
                {
                    job.Error = $"Process exited with code {process.ExitCode}.";
                }
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.EndedAt = DateTimeOffset.UtcNow;
                job.Error = ex.Message;
                OnLine(runtime, null, $"[ERROR] {ex}");
                _logger.LogError(ex, "Job {JobId} failed", id);
            }
        });

        return job;
    }

    public bool CancelJob(string id)
    {
        if (!_jobs.TryGetValue(id, out var runtime))
        {
            return false;
        }

        var job = runtime.Job;
        if (job.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled)
        {
            return true;
        }

        if (runtime.ProcessId <= 0)
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById(runtime.ProcessId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            job.Status = JobStatus.Canceled;
            job.EndedAt = DateTimeOffset.UtcNow;
            job.Error = "Canceled by user.";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel job {JobId}", id);
            return false;
        }
    }

    public int CancelAllRunningJobs()
    {
        var canceled = 0;
        foreach (var entry in _jobs)
        {
            if (CancelJob(entry.Key))
            {
                canceled++;
            }
        }
        return canceled;
    }

    private static void OnLine(RuntimeState state, StreamWriter? writer, string? line, bool countAsProcessOutput = true)
    {
        if (line is null)
        {
            return;
        }

        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        state.Lines.Enqueue(stamped);

        while (state.Lines.Count > 3000 && state.Lines.TryDequeue(out _))
        {
        }

        if (countAsProcessOutput)
        {
            state.LastProcessOutputAt = DateTimeOffset.UtcNow;
        }

        if (writer is not null)
        {
            lock (state.WriterLock)
            {
                writer.WriteLine(stamped);
                writer.Flush();
            }
        }
    }
}
