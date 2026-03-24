using System.Text.Json;
using OpenCd.Web.Models;

namespace OpenCd.Web.Services;

public sealed class OpenCdService
{
    public static readonly string[] ImageExts = [".tif", ".tiff", ".png", ".jpg", ".jpeg", ".bmp"];
    private readonly PathService _pathService;

    public OpenCdService(PathService pathService)
    {
        _pathService = pathService;
    }

    public IReadOnlyList<string> ListConfigs(string? keyword)
    {
        var cfgRoot = _pathService.ResolveInsideRepo("configs");
        if (!Directory.Exists(cfgRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(cfgRoot, "*.py", SearchOption.AllDirectories)
            .Select(_pathService.ToRepoRelative)
            .Where(x => string.IsNullOrWhiteSpace(keyword) || x.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .Take(2000)
            .ToList();
    }

    public IReadOnlyList<string> ListCheckpoints(string? keyword)
    {
        var workRoot = _pathService.ResolveInsideRepo("work_dirs");
        if (!Directory.Exists(workRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(workRoot, "*.pth", SearchOption.AllDirectories)
            .Where(x => string.IsNullOrWhiteSpace(keyword) || x.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(_pathService.ToRepoRelative)
            .Take(2000)
            .ToList();
    }

    public DatasetIndexResponse BuildDatasetIndex(string datasetRoot)
    {
        var root = _pathService.ResolveInsideRepo(datasetRoot);
        var splitInfos = new List<DatasetSplitInfo>();

        foreach (var split in new[] { "train", "val", "test" })
        {
            var samples = ListSampleNames(root, split).Take(500).ToList();
            splitInfos.Add(new DatasetSplitInfo(split, samples.Count, samples));
        }

        var spec = new DatasetSpecInfo(
            ["train/A", "train/B", "train/label", "val/A", "val/B", "val/label", "test/A", "test/B", "test/label"],
            ImageExts,
            [
                "A/B/label 三个目录必须同名配对。",
                "建议输入影像和标签统一为 uint8。",
                "可用 nodata，但建议写入明确值（如 0 或 255）并在配置中处理。",
                "常见裁块尺寸：256x256 或 512x512。"
            ]
        );

        return new DatasetIndexResponse(_pathService.ToRepoRelative(root), splitInfos, spec);
    }

    public SampleDetailResponse GetSampleDetail(string datasetRoot, string split, string sample)
    {
        var root = _pathService.ResolveInsideRepo(datasetRoot);
        var splitSafe = split.ToLowerInvariant();
        if (splitSafe is not ("train" or "val" or "test"))
        {
            throw new ArgumentException("split must be train/val/test");
        }

        string? ResolveSample(string sub)
        {
            var dir = Path.Combine(root, splitSafe, sub);
            if (!Directory.Exists(dir))
            {
                return null;
            }

            var file = Directory.EnumerateFiles(dir)
                .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals(sample, StringComparison.OrdinalIgnoreCase));
            return file;
        }

        var a = ResolveSample("A");
        var b = ResolveSample("B");
        var l = ResolveSample("label");

        return new SampleDetailResponse(
            sample,
            splitSafe,
            a is null ? null : _pathService.ToRepoRelative(a),
            b is null ? null : _pathService.ToRepoRelative(b),
            l is null ? null : _pathService.ToRepoRelative(l),
            a is null ? null : _pathService.BuildPreviewUrl(a),
            b is null ? null : _pathService.BuildPreviewUrl(b),
            l is null ? null : _pathService.BuildPreviewUrl(l));
    }

    public ScalarsResponse ReadScalars(string workDir)
    {
        var root = _pathService.ResolveInsideRepo(workDir);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Missing work dir: {workDir}");
        }

        var scalarFiles = Directory.EnumerateFiles(root, "scalars.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (scalarFiles.Count == 0)
        {
            return new ScalarsResponse(_pathService.ToRepoRelative(root), [], []);
        }

        var points = new List<MetricPoint>();
        var metricSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(scalarFiles[0]))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var obj = doc.RootElement;
            if (!obj.TryGetProperty("step", out var stepElem) && !obj.TryGetProperty("iter", out stepElem))
            {
                continue;
            }

            if (!stepElem.TryGetInt32(out var step))
            {
                continue;
            }

            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.NameEquals("iter") || prop.NameEquals("step"))
                {
                    continue;
                }

                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var d))
                {
                    values[prop.Name] = d;
                    metricSet.Add(prop.Name);
                }
            }

            if (values.Count > 0)
            {
                points.Add(new MetricPoint(step, values));
            }
        }

        var available = metricSet.OrderBy(x => x).ToList();
        return new ScalarsResponse(_pathService.ToRepoRelative(root), available, points.OrderBy(x => x.Step).ToList());
    }

    public FsListResponse ListPath(string? path, bool directoriesOnly, bool allFiles = false)
    {
        string target;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dataRoot = Path.Combine(_pathService.RepoRoot, "data");
            target = Directory.Exists(dataRoot) ? dataRoot : _pathService.RepoRoot;
        }
        else
        {
            target = _pathService.ResolveInsideRepo(path);
        }

        if (!Directory.Exists(target))
        {
            // Picker UX: if requested path does not exist (e.g. planned output dir),
            // fallback to nearest existing parent directory inside repo.
            var probe = target;
            while (!Directory.Exists(probe))
            {
                var probeParent = Directory.GetParent(probe);
                if (probeParent is null || !probeParent.FullName.StartsWith(_pathService.RepoRoot, StringComparison.Ordinal))
                {
                    throw new DirectoryNotFoundException(target);
                }
                probe = probeParent.FullName;
            }
            target = probe;
        }

        var entries = Directory.EnumerateFileSystemEntries(target)
            .Select(p => new
            {
                Full = p,
                IsDirectory = Directory.Exists(p),
                Ext = Path.GetExtension(p)
            })
            .Where(x => x.IsDirectory || !directoriesOnly)
            .Where(x => x.IsDirectory || allFiles || ImageExts.Contains(x.Ext, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(x => x.IsDirectory)
            .ThenBy(x => Path.GetFileName(x.Full), StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .Select(x => new FsEntry(
                Path.GetFileName(x.Full),
                _pathService.ToRepoRelative(x.Full),
                x.IsDirectory))
            .ToList();

        var parent = Directory.GetParent(target)?.FullName;
        string? parentRelative = null;
        if (parent is not null && parent.StartsWith(_pathService.RepoRoot, StringComparison.Ordinal))
        {
            parentRelative = _pathService.ToRepoRelative(parent);
        }

        return new FsListResponse(
            _pathService.ToRepoRelative(target),
            parentRelative,
            entries);
    }

    public MatchedPairResponse MatchPairFromPath(string path)
    {
        var selected = _pathService.ResolveInsideRepo(path);
        if (!File.Exists(selected))
        {
            throw new FileNotFoundException("File not found.", selected);
        }

        var selectedExt = Path.GetExtension(selected);
        if (!ImageExts.Contains(selectedExt, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Selected file is not an image.");
        }

        var folder = Directory.GetParent(selected)?.Name;
        if (folder is not ("A" or "B" or "label"))
        {
            throw new ArgumentException("Path must be under A/B/label folder.");
        }

        var pairRoot = Directory.GetParent(selected)!.Parent!;
        var split = pairRoot.Name;

        var sample = Path.GetFileNameWithoutExtension(selected);
        var a = FindSibling(pairRoot.FullName, "A", sample);
        var b = FindSibling(pairRoot.FullName, "B", sample);
        var label = FindSibling(pairRoot.FullName, "label", sample);

        if (a is null || b is null || label is null)
        {
            throw new InvalidOperationException(
                "Unable to match complete A/B/label pair by filename under current folder root.");
        }

        var labelRelative = _pathService.ToRepoRelative(label);
        var labelVectorUrl = $"/api/data/label-vectors?path={Uri.EscapeDataString(labelRelative)}";

        return new MatchedPairResponse(
            sample,
            split,
            _pathService.ToRepoRelative(a),
            _pathService.ToRepoRelative(b),
            labelRelative,
            _pathService.BuildPreviewUrl(a),
            _pathService.BuildPreviewUrl(b),
            _pathService.BuildPreviewUrl(label),
            labelVectorUrl);
    }

    public InterpretedPairResponse MatchPredictionFromPath(string path, string predRoot)
    {
        var pair = MatchPairFromPath(path);
        var predRootFull = _pathService.ResolveInsideRepo(predRoot);

        if (!Directory.Exists(predRootFull))
        {
            throw new DirectoryNotFoundException($"Prediction directory not found: {predRoot}");
        }

        var pred = FindPrediction(predRootFull, pair.Sample);
        if (pred is null)
        {
            throw new FileNotFoundException($"No prediction image matched sample '{pair.Sample}' under '{predRoot}'.");
        }

        var predRelative = _pathService.ToRepoRelative(pred);
        var predVectorUrl = $"/api/data/label-vectors?path={Uri.EscapeDataString(predRelative)}";

        return new InterpretedPairResponse(
            pair.Sample,
            pair.Split,
            pair.APath,
            pair.BPath,
            pair.LabelPath,
            predRelative,
            pair.APreviewUrl,
            pair.BPreviewUrl,
            pair.LabelPreviewUrl,
            _pathService.BuildPreviewUrl(pred),
            pair.LabelVectorUrl,
            predVectorUrl);
    }

    private static IEnumerable<string> ListSampleNames(string root, string split)
    {
        var dirs = new[] { "A", "B", "label" }
            .Select(sub => Path.Combine(root, split, sub))
            .ToList();

        if (dirs.Any(x => !Directory.Exists(x)))
        {
            return Enumerable.Empty<string>();
        }

        HashSet<string>? names = null;
        foreach (var dir in dirs)
        {
            var local = Directory.EnumerateFiles(dir)
                .Where(f => ImageExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            names = names is null ? local : names.Intersect(local, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return (names ?? []).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindSibling(string splitRoot, string subDir, string sample)
    {
        var dir = Path.Combine(splitRoot, subDir);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return Directory.EnumerateFiles(dir)
            .FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(sample, StringComparison.OrdinalIgnoreCase) &&
                ImageExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
    }

    private static string? FindPrediction(string predRoot, string sample)
    {
        var files = Directory.EnumerateFiles(predRoot, "*", SearchOption.AllDirectories)
            .Where(f => ImageExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Take(10000)
            .ToList();

        var exact = files.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Equals(sample, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return files.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).StartsWith(sample, StringComparison.OrdinalIgnoreCase));
    }
}
