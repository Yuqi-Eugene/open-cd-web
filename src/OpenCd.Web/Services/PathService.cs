using System.Text.RegularExpressions;
using System.Diagnostics;

namespace OpenCd.Web.Services;

public sealed class PathService
{
    private static readonly Regex UnsafePattern = new("[\\0]", RegexOptions.Compiled);
    public string RepoRoot { get; }
    private readonly object _pythonLock = new();
    private string? _cachedPreprocessPython;
    private string? _cachedOpenCdPython;

    public PathService(IHostEnvironment env)
    {
        RepoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));
    }

    public string ResolveInsideRepo(string relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
        {
            throw new ArgumentException("Path is required.");
        }

        if (UnsafePattern.IsMatch(relativeOrAbsolute))
        {
            throw new ArgumentException("Path contains invalid characters.");
        }

        var combined = Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.Combine(RepoRoot, relativeOrAbsolute);

        var full = Path.GetFullPath(combined);
        if (!full.StartsWith(RepoRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path is outside repository root.");
        }

        return full;
    }

    public string ToRepoRelative(string fullPath)
    {
        return Path.GetRelativePath(RepoRoot, fullPath).Replace('\\', '/');
    }

    public string GuessPythonExecutable()
    {
        var candidates = new[] { "python", "python3" };
        return candidates[0];
    }

    public string ResolvePythonExecutable(string? preferredPython)
    {
        if (!string.IsNullOrWhiteSpace(preferredPython))
        {
            return preferredPython!;
        }

        var detected = DetectPreprocessPython();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            return detected;
        }

        return GuessPythonExecutable();
    }

    public string ResolveOpenCdPython(string? preferredPython)
    {
        if (!string.IsNullOrWhiteSpace(preferredPython))
        {
            return preferredPython!;
        }

        var detected = DetectOpenCdPython();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            return detected;
        }

        return GuessPythonExecutable();
    }

    public string? DetectPreprocessPython(bool forceRefresh = false)
    {
        lock (_pythonLock)
        {
            if (!forceRefresh && !string.IsNullOrWhiteSpace(_cachedPreprocessPython))
            {
                return _cachedPreprocessPython;
            }

            var candidates = BuildPythonCandidates();
            foreach (var candidate in candidates)
            {
                if (CanImportPreprocessDeps(candidate))
                {
                    _cachedPreprocessPython = candidate;
                    return candidate;
                }
            }

            _cachedPreprocessPython = null;
            return null;
        }
    }

    public string? DetectOpenCdPython(bool forceRefresh = false)
    {
        lock (_pythonLock)
        {
            if (!forceRefresh && !string.IsNullOrWhiteSpace(_cachedOpenCdPython))
            {
                return _cachedOpenCdPython;
            }

            var candidates = BuildPythonCandidates();
            foreach (var candidate in candidates)
            {
                if (CanImportOpenCdDeps(candidate))
                {
                    _cachedOpenCdPython = candidate;
                    return candidate;
                }
            }

            _cachedOpenCdPython = null;
            return null;
        }
    }

    private IEnumerable<string> BuildPythonCandidates()
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                ordered.Add(trimmed);
            }
        }

        Add(Environment.GetEnvironmentVariable("PYTHON"));
        var condaPrefix = Environment.GetEnvironmentVariable("CONDA_PREFIX");
        if (!string.IsNullOrWhiteSpace(condaPrefix))
        {
            Add(Path.Combine(condaPrefix, "bin", "python"));
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            Add(Path.Combine(home, "miniconda3", "envs", "opencd", "bin", "python"));
            Add(Path.Combine(home, "anaconda3", "envs", "opencd", "bin", "python"));
            Add(Path.Combine(home, "mambaforge", "envs", "opencd", "bin", "python"));
            Add(Path.Combine(home, "miniforge3", "envs", "opencd", "bin", "python"));
            Add(Path.Combine(RepoRoot, ".venv", "bin", "python"));
            Add(Path.Combine(RepoRoot, "venv", "bin", "python"));
        }

        Add("python");
        Add("python3");
        Add("/opt/homebrew/Caskroom/miniconda/base/envs/opencd/bin/python");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add("which -a python python3 2>/dev/null | awk '!seen[$0]++'");
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1500);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    Add(line);
                }
            }
        }
        catch
        {
            // Ignore probing failures; fallback candidates still work.
        }

        // Conda envs often contain the exact runtime user expects (e.g. opencd).
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "conda",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("info");
            psi.ArgumentList.Add("--envs");
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.Length == 0)
                    {
                        continue;
                    }

                    var cols = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var envPath = cols[^1];
                    var py = Path.Combine(envPath, "bin", "python");
                    Add(py);
                }
            }
        }
        catch
        {
            // Conda may not be installed; ignore.
        }

        return ordered;
    }

    private static bool CanImportPreprocessDeps(string python)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import numpy, rasterio, tqdm; print('ok')");

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            if (!proc.WaitForExit(10000))
            {
                try { proc.Kill(true); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanImportOpenCdDeps(string python)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import mmengine, mmcv, opencd; print('ok')");

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            if (!proc.WaitForExit(12000))
            {
                try { proc.Kill(true); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public string BuildPreviewUrl(string fullPath)
    {
        var encoded = Uri.EscapeDataString(ToRepoRelative(fullPath));
        return $"/api/data/preview?path={encoded}";
    }

    public bool SupportsAmpTraining(string python)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import torch; import mmengine.utils.dl_utils as d; ok = bool(torch.cuda.is_available() or d.is_npu_available() or d.is_mlu_available() or d.is_musa_available()); print('1' if ok else '0')");

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(8000))
            {
                try { proc.Kill(true); } catch { }
                return false;
            }

            return proc.ExitCode == 0 && output.Trim().EndsWith("1", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
