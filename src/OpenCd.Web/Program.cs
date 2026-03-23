using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;
using OpenCd.Web.Models;
using OpenCd.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.WriteIndented = true;
});

builder.Services.AddSingleton<PathService>();
builder.Services.AddSingleton<JobRunnerService>();
builder.Services.AddSingleton<OpenCdService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (PathService paths) => new
{
    ok = true,
    repoRoot = paths.RepoRoot,
    serverTime = DateTimeOffset.Now
});

app.MapGet("/api/system/choose-directory", async (string? startPath, bool? simple, PathService paths) =>
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        return Results.BadRequest("Only macOS is supported for Finder directory picker.");
    }

    var useSimple = simple ?? false;
    string script;

    if (useSimple)
    {
        // Most compatible mode: no default location.
        script = """
set chosenFolder to choose folder with prompt "请选择目录"
POSIX path of chosenFolder
""";
    }
    else
    {
        var startDir = string.IsNullOrWhiteSpace(startPath)
            ? paths.RepoRoot
            : paths.ResolveInsideRepo(startPath);

        // Finder default location must be an existing alias/folder.
        var defaultDir = startDir;
        while (!Directory.Exists(defaultDir))
        {
            var parent = Directory.GetParent(defaultDir);
            if (parent is null)
            {
                defaultDir = paths.RepoRoot;
                break;
            }
            defaultDir = parent.FullName;
        }

        script = $"""
set defaultLocationAlias to (POSIX file "{defaultDir}/") as alias
set chosenFolder to choose folder with prompt "请选择目录" default location defaultLocationAlias
POSIX path of chosenFolder
""";
    }

    var psi = new ProcessStartInfo
    {
        FileName = "osascript",
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    psi.ArgumentList.Add("-e");
    psi.ArgumentList.Add(script);

    using var process = Process.Start(psi);
    if (process is null)
    {
        return Results.Problem("Failed to launch Finder chooser.");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        var err = string.IsNullOrWhiteSpace(stderr) ? "Chooser canceled or failed." : stderr.Trim();
        return Results.BadRequest(err);
    }

    var chosen = stdout.Trim();
    if (string.IsNullOrWhiteSpace(chosen) || !Directory.Exists(chosen))
    {
        return Results.BadRequest("Invalid directory selected.");
    }

    var full = Path.GetFullPath(chosen);
    if (!full.StartsWith(paths.RepoRoot, StringComparison.Ordinal))
    {
        return Results.BadRequest("Selected directory is outside repository root.");
    }

    return Results.Ok(new
    {
        Path = paths.ToRepoRelative(full)
    });
});

app.MapGet("/api/system/choose-file", async (string? startPath) =>
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        return Results.BadRequest("Only macOS is supported for Finder file picker.");
    }

    var defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (!string.IsNullOrWhiteSpace(startPath))
    {
        if (Directory.Exists(startPath))
        {
            defaultDir = startPath;
        }
        else
        {
            defaultDir = Path.GetDirectoryName(startPath) ?? defaultDir;
        }
    }

    if (!Directory.Exists(defaultDir))
    {
        defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    var script = $"""
set defaultLocationAlias to (POSIX file "{defaultDir}/") as alias
set chosenFile to choose file with prompt "请选择文件" default location defaultLocationAlias
POSIX path of chosenFile
""";

    var psi = new ProcessStartInfo
    {
        FileName = "osascript",
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    psi.ArgumentList.Add("-e");
    psi.ArgumentList.Add(script);

    using var process = Process.Start(psi);
    if (process is null)
    {
        return Results.Problem("Failed to launch Finder chooser.");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        var err = string.IsNullOrWhiteSpace(stderr) ? "Chooser canceled or failed." : stderr.Trim();
        return Results.BadRequest(err);
    }

    var chosen = stdout.Trim();
    if (string.IsNullOrWhiteSpace(chosen) || !File.Exists(chosen))
    {
        return Results.BadRequest("Invalid file selected.");
    }

    return Results.Ok(new { Path = Path.GetFullPath(chosen) });
});

app.MapGet("/api/system/python/detect", (bool? refresh, PathService paths) =>
{
    var python = paths.DetectPreprocessPython(refresh ?? false);
    return Results.Ok(new
    {
        Found = !string.IsNullOrWhiteSpace(python),
        Python = python
    });
});

app.MapGet("/api/opencd/configs", (string? keyword, OpenCdService service) =>
{
    return Results.Ok(service.ListConfigs(keyword));
});

app.MapGet("/api/opencd/checkpoints", (string? keyword, OpenCdService service) =>
{
    return Results.Ok(service.ListCheckpoints(keyword));
});

app.MapGet("/api/opencd/scalars", (string workDir, OpenCdService service) =>
{
    return Results.Ok(service.ReadScalars(workDir));
});

app.MapGet("/api/data/index", (string datasetRoot, OpenCdService service) =>
{
    return Results.Ok(service.BuildDatasetIndex(datasetRoot));
});

app.MapGet("/api/fs/list", (string? path, bool? dirsOnly, OpenCdService service) =>
{
    return Results.Ok(service.ListPath(path, dirsOnly ?? false));
});

app.MapGet("/api/data/sample", (string datasetRoot, string split, string sample, OpenCdService service) =>
{
    return Results.Ok(service.GetSampleDetail(datasetRoot, split, sample));
});

app.MapGet("/api/data/match", (string path, OpenCdService service) =>
{
    return Results.Ok(service.MatchPairFromPath(path));
});

app.MapGet("/api/data/match-infer", (string path, string predRoot, OpenCdService service) =>
{
    return Results.Ok(service.MatchPredictionFromPath(path, predRoot));
});

app.MapGet("/api/data/raw", (string path, PathService paths) =>
{
    var fullPath = paths.ResolveInsideRepo(path);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(fullPath, out var contentType))
    {
        contentType = "application/octet-stream";
    }

    return Results.File(fullPath, contentType, enableRangeProcessing: true);
});

app.MapGet("/api/data/preview", async (string path, PathService paths) =>
{
    var fullPath = paths.ResolveInsideRepo(path);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    var script = paths.ResolveInsideRepo("src/OpenCd.Web/scripts/render_preview.py");
    var tmpFile = Path.Combine(Path.GetTempPath(), $"ocd_preview_{Guid.NewGuid():N}.png");
    var isLabel = fullPath.Replace('\\', '/').Contains("/label/", StringComparison.OrdinalIgnoreCase);

    var python = paths.ResolvePythonExecutable(null);
    var psi = new ProcessStartInfo
    {
        FileName = python,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = paths.RepoRoot
    };

    psi.ArgumentList.Add(script);
    psi.ArgumentList.Add("--input");
    psi.ArgumentList.Add(fullPath);
    psi.ArgumentList.Add("--output");
    psi.ArgumentList.Add(tmpFile);
    if (isLabel)
    {
        psi.ArgumentList.Add("--label");
    }

    using var process = Process.Start(psi);
    if (process is null)
    {
        return Results.Problem("Unable to start preview process.");
    }

    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        return Results.Problem($"Preview failed (python: {python}): {stderr}");
    }

    var bytes = await File.ReadAllBytesAsync(tmpFile);
    File.Delete(tmpFile);

    return Results.File(bytes, "image/png");
});

app.MapGet("/api/data/label-vectors", async (string path, PathService paths) =>
{
    var fullPath = paths.ResolveInsideRepo(path);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    var script = paths.ResolveInsideRepo("src/OpenCd.Web/scripts/label_vectors.py");
    var tmpFile = Path.Combine(Path.GetTempPath(), $"ocd_vectors_{Guid.NewGuid():N}.json");

    try
    {
        var python = paths.ResolvePythonExecutable(null);
        var psi = new ProcessStartInfo
        {
            FileName = python,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = paths.RepoRoot
        };

        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--input");
        psi.ArgumentList.Add(fullPath);
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(tmpFile);

        using var process = Process.Start(psi);
        if (process is null)
        {
            return Results.Problem("Unable to start label vector process.");
        }

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            return Results.Problem($"Label vector process failed (python: {python}): {stderr}");
        }

        var text = await File.ReadAllTextAsync(tmpFile);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        var width = root.GetProperty("Width").GetInt32();
        var height = root.GetProperty("Height").GetInt32();
        var features = new List<LabelPolygonFeature>();
        foreach (var f in root.GetProperty("Features").EnumerateArray())
        {
            var classValue = f.GetProperty("ClassValue").GetInt32();
            var rings = new List<IReadOnlyList<Point2D>>();
            foreach (var ringElem in f.GetProperty("Rings").EnumerateArray())
            {
                var points = ringElem.EnumerateArray()
                    .Select(p => new Point2D(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble()))
                    .ToList();
                rings.Add(points);
            }
            features.Add(new LabelPolygonFeature(classValue, rings));
        }

        return Results.Ok(new LabelVectorsResponse(paths.ToRepoRelative(fullPath), width, height, features));
    }
    finally
    {
        if (File.Exists(tmpFile))
        {
            File.Delete(tmpFile);
        }
    }
});

app.MapGet("/api/jobs", (JobRunnerService jobs) => Results.Ok(jobs.ListJobs()));

app.MapGet("/api/jobs/{id}", (string id, JobRunnerService jobs) =>
{
    var job = jobs.GetJob(id);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapGet("/api/jobs/{id}/log", (string id, int? tail, JobRunnerService jobs) =>
{
    var snapshot = jobs.GetLog(id, tail ?? 300);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
});

app.MapPost("/api/jobs/{id}/cancel", (string id, JobRunnerService jobs) =>
{
    var job = jobs.GetJob(id);
    if (job is null)
    {
        return Results.NotFound();
    }

    return jobs.CancelJob(id)
        ? Results.Ok(new { Message = "Job canceled.", JobId = id })
        : Results.Problem("Failed to cancel job.");
});

app.MapPost("/api/jobs/cancel-all", (JobRunnerService jobs) =>
{
    var count = jobs.CancelAllRunningJobs();
    return Results.Ok(new { Canceled = count });
});

app.MapPost("/api/preprocess/batch-name", (BatchRenameRequest req, PathService paths, JobRunnerService jobs) =>
{
    var script = paths.ResolveInsideRepo("dataprep/batch_name.py");
    var args = new List<string> { script, "--dir", paths.ResolveInsideRepo(req.Directory) };

    AddOption(args, "--pattern", req.Pattern);
    AddOption(args, "--ext", req.Ext);
    AddOption(args, "--add-prefix", req.AddPrefix);
    AddOption(args, "--add-suffix", req.AddSuffix);
    AddOption(args, "--remove-prefix", req.RemovePrefix);
    AddOption(args, "--remove-suffix", req.RemoveSuffix);
    AddOption(args, "--regex-find", req.RegexFind);
    AddOption(args, "--regex-repl", req.RegexRepl);

    if (req.Recursive) args.Add("--recursive");
    if (req.Apply) args.Add("--apply");

    var python = paths.ResolvePythonExecutable(req.Python);
    var job = jobs.StartJob("preprocess.batch-name", python, args, paths.RepoRoot);
    return Results.Ok(job);
});

app.MapPost("/api/preprocess/uint8", (UInt8PreprocessRequest req, PathService paths, JobRunnerService jobs) =>
{
    var script = paths.ResolveInsideRepo("dataprep/prep_uint8_nodata0.py");
    var args = new List<string>
    {
        script,
        "--in_dir", paths.ResolveInsideRepo(req.InDir),
        "--out_dir", paths.ResolveInsideRepo(req.OutDir),
        "--clip_p2", req.ClipP2.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--clip_p98", req.ClipP98.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--map_nodata_to", req.MapNodataTo.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--nodata_value", req.NodataValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    if (req.NoScale) args.Add("--no_scale");

    var python = paths.ResolvePythonExecutable(req.Python);
    var job = jobs.StartJob("preprocess.uint8", python, args, paths.RepoRoot);
    return Results.Ok(job);
});

app.MapPost("/api/preprocess/split", (SplitDatasetRequest req, PathService paths, JobRunnerService jobs) =>
{
    var script = paths.ResolveInsideRepo("dataprep/split_train_val_test.py");
    var args = new List<string>
    {
        script,
        "--in_dir", paths.ResolveInsideRepo(req.InDir),
        "--out_dir", paths.ResolveInsideRepo(req.OutDir),
        "--train", req.Train.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--val", req.Val.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--test", req.Test.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--seed", req.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--method", req.Method
    };

    AddOption(args, "--ext", req.Ext);
    if (req.Apply) args.Add("--apply");

    var python = paths.ResolvePythonExecutable(req.Python);
    var job = jobs.StartJob("preprocess.split", python, args, paths.RepoRoot);
    return Results.Ok(job);
});

app.MapPost("/api/opencd/train", (TrainRequest req, PathService paths, JobRunnerService jobs) =>
{
    var script = paths.ResolveInsideRepo("tools/train.py");
    var python = paths.ResolveOpenCdPython(req.Python);
    var env = BuildOpenCdEnv();

    if (req.Amp && !paths.SupportsAmpTraining(python))
    {
        return Results.BadRequest("当前 Python 环境未检测到可用 GPU/NPU/MLU/MUSA，无法启用 AMP。请取消勾选 AMP 后重试。");
    }

    var args = new List<string>
    {
        script,
        paths.ResolveInsideRepo(req.ConfigPath),
        "--work-dir", paths.ResolveInsideRepo(req.WorkDir)
    };

    if (req.Amp) args.Add("--amp");
    AddOption(args, "--resume", req.ResumeFrom is null ? null : paths.ResolveInsideRepo(req.ResumeFrom));
    AddDatasetRootCfgOptions(args, paths, req.DatasetRoot);
    AddDefaultTrainLogInterval(args, req.ExtraArgs, 10);
    AddMacCpuStabilityCfgOptions(args, req.ExtraArgs);
    args.AddRange(SplitArgs(req.ExtraArgs));

    var job = jobs.StartJob("opencd.train", python, args, paths.RepoRoot, env);
    return Results.Ok(job);
});

app.MapPost("/api/opencd/test", (EvalRequest req, PathService paths, JobRunnerService jobs) =>
{
    var script = paths.ResolveInsideRepo("tools/test.py");
    var env = BuildOpenCdEnv();
    var args = new List<string>
    {
        script,
        paths.ResolveInsideRepo(req.ConfigPath),
        paths.ResolveInsideRepo(req.CheckpointPath)
    };

    AddOption(args, "--work-dir", req.WorkDir is null ? null : paths.ResolveInsideRepo(req.WorkDir));
    AddOption(args, "--task", req.Task);

    if (req.ShowDirPred && !string.IsNullOrWhiteSpace(req.Out))
    {
        AddOption(args, "--show-dir", paths.ResolveInsideRepo(req.Out));
    }

    AddDatasetRootCfgOptions(args, paths, req.DatasetRoot);
    args.AddRange(SplitArgs(req.ExtraArgs));

    var python = paths.ResolveOpenCdPython(req.Python);
    var job = jobs.StartJob("opencd.test", python, args, paths.RepoRoot, env);
    return Results.Ok(job);
});

app.MapPost("/api/opencd/validate", (EvalRequest req, PathService paths, JobRunnerService jobs) =>
{
    var script = paths.ResolveInsideRepo("tools/test.py");
    var env = BuildOpenCdEnv();
    var args = new List<string>
    {
        script,
        paths.ResolveInsideRepo(req.ConfigPath),
        paths.ResolveInsideRepo(req.CheckpointPath)
    };

    AddOption(args, "--work-dir", req.WorkDir is null ? null : paths.ResolveInsideRepo(req.WorkDir));
    AddDatasetRootCfgOptions(args, paths, req.DatasetRoot);
    args.AddRange(SplitArgs(req.ExtraArgs));

    var python = paths.ResolveOpenCdPython(req.Python);
    var job = jobs.StartJob("opencd.validate", python, args, paths.RepoRoot, env);
    return Results.Ok(job);
});

app.Run();

static void AddOption(ICollection<string> args, string name, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return;
    }

    args.Add(name);
    args.Add(value);
}

static IReadOnlyDictionary<string, string>? BuildOpenCdEnv()
{
    if (!OperatingSystem.IsMacOS())
    {
        return null;
    }

    return new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Open-CD currently has ops with poor/partial MPS coverage; force CPU on macOS for stability.
        ["OPENCD_FORCE_CPU"] = "1",
        ["PYTORCH_MPS_DISABLE"] = "1",
        ["PYTORCH_ENABLE_MPS_FALLBACK"] = "1",
        // Keep BLAS/OpenMP thread usage conservative to reduce macOS native crashes (e.g. SIGBUS 138).
        ["OMP_NUM_THREADS"] = "1",
        ["MKL_NUM_THREADS"] = "1",
        ["VECLIB_MAXIMUM_THREADS"] = "1",
        ["NUMEXPR_NUM_THREADS"] = "1",
        ["OPENBLAS_NUM_THREADS"] = "1"
    };
}

static void AddDefaultTrainLogInterval(ICollection<string> args, string? extraArgs, int defaultInterval)
{
    if (!string.IsNullOrWhiteSpace(extraArgs) &&
        extraArgs.Contains("default_hooks.logger.interval", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    args.Add("--cfg-options");
    args.Add($"default_hooks.logger.interval={defaultInterval}");
}

static void AddMacCpuStabilityCfgOptions(ICollection<string> args, string? extraArgs)
{
    if (!OperatingSystem.IsMacOS())
    {
        return;
    }

    var raw = extraArgs ?? string.Empty;

    void AddCfgIfMissing(string key, string value)
    {
        if (raw.Contains(key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        args.Add("--cfg-options");
        args.Add($"{key}={value}");
    }

    // macOS + CPU 下 r18 类配置更容易触发底层崩溃（ExitCode 138 / SIGBUS），
    // 默认使用更保守的多进程与 batch 设置。
    AddCfgIfMissing("env_cfg.mp_cfg.mp_start_method", "spawn");
    AddCfgIfMissing("train_dataloader.num_workers", "0");
    AddCfgIfMissing("val_dataloader.num_workers", "0");
    AddCfgIfMissing("test_dataloader.num_workers", "0");
    AddCfgIfMissing("train_dataloader.persistent_workers", "False");
    AddCfgIfMissing("val_dataloader.persistent_workers", "False");
    AddCfgIfMissing("test_dataloader.persistent_workers", "False");
    AddCfgIfMissing("train_dataloader.batch_size", "1");
    AddCfgIfMissing("train_dataloader.dataset.serialize_data", "False");
    AddCfgIfMissing("val_dataloader.dataset.serialize_data", "False");
    AddCfgIfMissing("test_dataloader.dataset.serialize_data", "False");
}

static IEnumerable<string> SplitArgs(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return [];
    }

    var result = new List<string>();
    var sb = new StringBuilder();
    var quote = '\0';

    foreach (var c in raw)
    {
        if ((c == '\'' || c == '"'))
        {
            if (quote == '\0')
            {
                quote = c;
                continue;
            }

            if (quote == c)
            {
                quote = '\0';
                continue;
            }
        }

        if (char.IsWhiteSpace(c) && quote == '\0')
        {
            if (sb.Length > 0)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            continue;
        }

        sb.Append(c);
    }

    if (sb.Length > 0)
    {
        result.Add(sb.ToString());
    }

    return result;
}

static void AddDatasetRootCfgOptions(ICollection<string> args, PathService paths, string? datasetRoot)
{
    if (string.IsNullOrWhiteSpace(datasetRoot))
    {
        return;
    }

    var full = paths.ResolveInsideRepo(datasetRoot).Replace('\\', '/');
    args.Add("--cfg-options");
    args.Add($"data_root={full}");
    args.Add($"train_dataloader.dataset.data_root={full}");
    args.Add($"val_dataloader.dataset.data_root={full}");
    args.Add($"test_dataloader.dataset.data_root={full}");

    // Auto-detect file suffix from selected dataset root to avoid config suffix mismatch
    // (e.g. config expects .tif but dataset actually contains .png).
    var imgSuffix = DetectFirstImageSuffix(full, Path.Combine("train", "A"));
    if (!string.IsNullOrWhiteSpace(imgSuffix))
    {
        args.Add($"train_dataloader.dataset.img_suffix={imgSuffix}");
        args.Add($"val_dataloader.dataset.img_suffix={imgSuffix}");
        args.Add($"test_dataloader.dataset.img_suffix={imgSuffix}");

        if (imgSuffix is not ".tif" and not ".tiff")
        {
            // Some Open-CD configs hardcode tifffile in train/val/test/tta pipelines.
            // For png/jpg datasets, force cv2 backend to avoid "not a TIFF file".
            args.Add("train_dataloader.dataset.pipeline.0.imdecode_backend=cv2");
            args.Add("train_dataloader.dataset.pipeline.1.imdecode_backend=cv2");
            args.Add("val_dataloader.dataset.pipeline.0.imdecode_backend=cv2");
            args.Add("val_dataloader.dataset.pipeline.2.imdecode_backend=cv2");
            args.Add("test_dataloader.dataset.pipeline.0.imdecode_backend=cv2");
            args.Add("test_dataloader.dataset.pipeline.2.imdecode_backend=cv2");
            args.Add("train_pipeline.0.imdecode_backend=cv2");
            args.Add("train_pipeline.1.imdecode_backend=cv2");
            args.Add("test_pipeline.0.imdecode_backend=cv2");
            args.Add("test_pipeline.2.imdecode_backend=cv2");
            args.Add("tta_pipeline.0.imdecode_backend=cv2");
            args.Add("tta_pipeline.1.transforms.2.0.imdecode_backend=cv2");
        }
    }

    var segSuffix = DetectFirstImageSuffix(full, Path.Combine("train", "label"));
    if (!string.IsNullOrWhiteSpace(segSuffix))
    {
        args.Add($"train_dataloader.dataset.seg_map_suffix={segSuffix}");
        args.Add($"val_dataloader.dataset.seg_map_suffix={segSuffix}");
        args.Add($"test_dataloader.dataset.seg_map_suffix={segSuffix}");
    }
}

static string? DetectFirstImageSuffix(string datasetRoot, string subDir)
{
    var dir = Path.Combine(datasetRoot, subDir);
    if (!Directory.Exists(dir))
    {
        return null;
    }

    var file = Directory.EnumerateFiles(dir)
        .FirstOrDefault(f => OpenCdService.ImageExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

    if (string.IsNullOrWhiteSpace(file))
    {
        return null;
    }

    return Path.GetExtension(file).ToLowerInvariant();
}
