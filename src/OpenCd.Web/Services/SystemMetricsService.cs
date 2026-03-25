using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace OpenCd.Web.Services;

public sealed class SystemMetricsService
{
    private readonly object _lock = new();
    private long _lastCpuTotal;
    private long _lastCpuIdle;
    private DateTimeOffset _lastNetworkAt = DateTimeOffset.UtcNow;
    private long _lastRxBytes;
    private long _lastTxBytes;
    private bool _networkPrimed;
    private double? _macTotalMemoryMb;

    public object GetSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var (cpuPercent, memoryPercent, memoryUsedMb, memoryTotalMb) = ReadSystemUsage();
        var (rxBytesPerSec, txBytesPerSec) = ReadNetworkRate(now);

        return new
        {
            Timestamp = now,
            CpuPercent = Math.Round(cpuPercent, 2),
            MemoryPercent = Math.Round(memoryPercent, 2),
            MemoryUsedMb = Math.Round(memoryUsedMb, 1),
            MemoryTotalMb = Math.Round(memoryTotalMb, 1),
            NetworkRxBytesPerSec = Math.Round(rxBytesPerSec, 2),
            NetworkTxBytesPerSec = Math.Round(txBytesPerSec, 2)
        };
    }

    private (double cpuPercent, double memoryPercent, double memoryUsedMb, double memoryTotalMb) ReadSystemUsage()
    {
        if (OperatingSystem.IsLinux())
        {
            var cpu = ReadLinuxCpuPercent();
            var (memPct, memUsedMb, memTotalMb) = ReadLinuxMemory();
            return (cpu, memPct, memUsedMb, memTotalMb);
        }

        if (OperatingSystem.IsMacOS())
        {
            return ReadMacUsage();
        }

        var proc = Environment.ProcessId;
        _ = proc;
        return (0, 0, 0, 0);
    }

    private (double cpuPercent, double memoryPercent, double memoryUsedMb, double memoryTotalMb) ReadMacUsage()
    {
        try
        {
            var topOut = RunCommand("/usr/bin/top", "-l", "1", "-n", "0");
            if (string.IsNullOrWhiteSpace(topOut))
            {
                return (0, 0, 0, 0);
            }

            var cpu = 0d;
            var cpuMatch = Regex.Match(topOut, @"CPU usage:\s*[\d.]+%\s*user,\s*[\d.]+%\s*sys,\s*([\d.]+)%\s*idle", RegexOptions.IgnoreCase);
            if (cpuMatch.Success &&
                double.TryParse(cpuMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var idlePct))
            {
                cpu = Math.Clamp(100.0 - idlePct, 0, 100);
            }

            // Example:
            // PhysMem: 8192M used (1780M wired, 0B compressor), 86M unused.
            var memMatch = Regex.Match(topOut, @"PhysMem:\s*([^,]+)\s*used.*?,\s*([^,]+)\s*unused", RegexOptions.IgnoreCase);
            if (!memMatch.Success)
            {
                return (cpu, 0, 0, 0);
            }

            var usedMb = ParseMemoryToMb(memMatch.Groups[1].Value);
            var unusedMb = ParseMemoryToMb(memMatch.Groups[2].Value);
            var totalMb = GetMacTotalMemoryMb();
            if (totalMb <= 0)
            {
                totalMb = Math.Max(usedMb + unusedMb, 0);
            }

            if (totalMb <= 0)
            {
                return (cpu, 0, 0, 0);
            }

            var memPct = Math.Clamp(usedMb * 100.0 / totalMb, 0, 100);
            return (cpu, memPct, usedMb, totalMb);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    private double ReadLinuxCpuPercent()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line))
            {
                return 0;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !string.Equals(parts[0], "cpu", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            long Parse(int idx) => idx < parts.Length ? long.Parse(parts[idx], CultureInfo.InvariantCulture) : 0L;

            var user = Parse(1);
            var nice = Parse(2);
            var system = Parse(3);
            var idle = Parse(4);
            var iowait = Parse(5);
            var irq = Parse(6);
            var softirq = Parse(7);
            var steal = Parse(8);

            var total = user + nice + system + idle + iowait + irq + softirq + steal;
            var idleTotal = idle + iowait;

            lock (_lock)
            {
                if (_lastCpuTotal == 0 || _lastCpuIdle == 0)
                {
                    _lastCpuTotal = total;
                    _lastCpuIdle = idleTotal;
                    return 0;
                }

                var totalDelta = total - _lastCpuTotal;
                var idleDelta = idleTotal - _lastCpuIdle;
                _lastCpuTotal = total;
                _lastCpuIdle = idleTotal;

                if (totalDelta <= 0)
                {
                    return 0;
                }

                var busy = totalDelta - idleDelta;
                var pct = busy * 100.0 / totalDelta;
                return Math.Clamp(pct, 0, 100);
            }
        }
        catch
        {
            return 0;
        }
    }

    private static (double memoryPercent, double memoryUsedMb, double memoryTotalMb) ReadLinuxMemory()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            long memTotalKb = 0;
            long memAvailableKb = 0;

            foreach (var raw in lines)
            {
                if (raw.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    memTotalKb = ParseMemValue(raw);
                }
                else if (raw.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    memAvailableKb = ParseMemValue(raw);
                }
            }

            if (memTotalKb <= 0)
            {
                return (0, 0, 0);
            }

            var usedKb = Math.Max(0, memTotalKb - memAvailableKb);
            var percent = usedKb * 100.0 / memTotalKb;
            return (
                Math.Clamp(percent, 0, 100),
                usedKb / 1024.0,
                memTotalKb / 1024.0
            );
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private static long ParseMemValue(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return 0;
        }
        return long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private (double rxBytesPerSec, double txBytesPerSec) ReadNetworkRate(DateTimeOffset now)
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    !n.Description.Contains("docker", StringComparison.OrdinalIgnoreCase))
                .ToList();

            long rx = 0;
            long tx = 0;
            foreach (var nic in interfaces)
            {
                var stat = nic.GetIPv4Statistics();
                rx += stat.BytesReceived;
                tx += stat.BytesSent;
            }

            lock (_lock)
            {
                if (!_networkPrimed)
                {
                    _networkPrimed = true;
                    _lastRxBytes = rx;
                    _lastTxBytes = tx;
                    _lastNetworkAt = now;
                    return (0, 0);
                }

                var dt = (now - _lastNetworkAt).TotalSeconds;
                if (dt <= 0)
                {
                    return (0, 0);
                }

                var rxRate = (rx - _lastRxBytes) / dt;
                var txRate = (tx - _lastTxBytes) / dt;
                _lastRxBytes = rx;
                _lastTxBytes = tx;
                _lastNetworkAt = now;
                return (Math.Max(0, rxRate), Math.Max(0, txRate));
            }
        }
        catch
        {
            return (0, 0);
        }
    }

    private static string RunCommand(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        using var process = Process.Start(psi);
        if (process is null)
        {
            return string.Empty;
        }
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(2000);
        return stdout;
    }

    private double GetMacTotalMemoryMb()
    {
        if (_macTotalMemoryMb.HasValue)
        {
            return _macTotalMemoryMb.Value;
        }

        try
        {
            var output = RunCommand("/usr/sbin/sysctl", "-n", "hw.memsize").Trim();
            if (long.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) && bytes > 0)
            {
                _macTotalMemoryMb = bytes / 1024d / 1024d;
                return _macTotalMemoryMb.Value;
            }
        }
        catch
        {
            // ignored
        }

        _macTotalMemoryMb = 0;
        return 0;
    }

    private static double ParseMemoryToMb(string raw)
    {
        var m = Regex.Match(raw ?? string.Empty, @"([\d.]+)\s*([KMGT]?)(?:i?B)?", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return 0;
        }

        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var unit = m.Groups[2].Value.ToUpperInvariant();
        return unit switch
        {
            "K" => value / 1024d,
            "M" => value,
            "G" => value * 1024d,
            "T" => value * 1024d * 1024d,
            _ => value / 1024d / 1024d // bytes
        };
    }
}
