namespace OpenCd.Web.Models;

public sealed record BatchRenameRequest(
    string Directory,
    bool Recursive,
    string? Pattern,
    string? Ext,
    string? AddPrefix,
    string? AddSuffix,
    string? RemovePrefix,
    string? RemoveSuffix,
    string? RegexFind,
    string? RegexRepl,
    bool Apply = true,
    string? Python = null);

public sealed record UInt8PreprocessRequest(
    string InDir,
    string OutDir,
    double ClipP2 = 2,
    double ClipP98 = 98,
    bool NoScale = false,
    double MapNodataTo = 0,
    double NodataValue = 0,
    string? Python = null);

public sealed record SplitDatasetRequest(
    string InDir,
    string OutDir,
    double Train = 0.7,
    double Val = 0.15,
    double Test = 0.15,
    int Seed = 42,
    string Method = "copy",
    string? Ext = ".tif,.tiff,.png",
    bool Apply = true,
    string? Python = null);

public sealed record TrainRequest(
    string ConfigPath,
    string WorkDir,
    string? DatasetRoot = null,
    bool Amp = false,
    string? ResumeFrom = null,
    string? ExtraArgs = null,
    string? Python = null);

public sealed record EvalRequest(
    string ConfigPath,
    string CheckpointPath,
    string? DatasetRoot = null,
    string? WorkDir = null,
    string? Task = null,
    string? Out = null,
    bool ShowDirPred = false,
    string? ExtraArgs = null,
    string? Python = null);

public sealed record DatasetIndexResponse(
    string DatasetRoot,
    IReadOnlyList<DatasetSplitInfo> Splits,
    DatasetSpecInfo Spec);

public sealed record DatasetSplitInfo(
    string Name,
    int Count,
    IReadOnlyList<string> Samples);

public sealed record SampleDetailResponse(
    string Sample,
    string Split,
    string? APath,
    string? BPath,
    string? LabelPath,
    string? APreviewUrl,
    string? BPreviewUrl,
    string? LabelPreviewUrl);

public sealed record MetricPoint(int Step, Dictionary<string, double> Values);

public sealed record ScalarsResponse(
    string WorkDir,
    IReadOnlyList<string> AvailableMetrics,
    IReadOnlyList<MetricPoint> Points);

public sealed record DatasetSpecInfo(
    IReadOnlyList<string> RequiredDirs,
    IReadOnlyList<string> SupportedImageExt,
    IReadOnlyList<string> Notes);

public sealed record FsEntry(
    string Name,
    string Path,
    bool IsDirectory);

public sealed record FsListResponse(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<FsEntry> Entries);

public sealed record MatchedPairResponse(
    string Sample,
    string Split,
    string APath,
    string BPath,
    string LabelPath,
    string APreviewUrl,
    string BPreviewUrl,
    string LabelPreviewUrl,
    string LabelVectorUrl);

public sealed record InterpretedPairResponse(
    string Sample,
    string Split,
    string APath,
    string BPath,
    string LabelPath,
    string PredPath,
    string APreviewUrl,
    string BPreviewUrl,
    string LabelPreviewUrl,
    string PredPreviewUrl,
    string LabelVectorUrl,
    string PredVectorUrl);

public sealed record LabelVectorsResponse(
    string Path,
    int Width,
    int Height,
    IReadOnlyList<LabelPolygonFeature> Features);

public sealed record LabelPolygonFeature(
    int ClassValue,
    IReadOnlyList<IReadOnlyList<Point2D>> Rings);

public sealed record Point2D(
    double X,
    double Y);
