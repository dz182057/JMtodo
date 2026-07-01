namespace TodoDesktopApp.Models;

public sealed class ImportPreviewResult
{
    public IReadOnlyList<ImportTaskPreview> RootTasks { get; init; } = Array.Empty<ImportTaskPreview>();

    public int SkippedDuplicateCount { get; init; }

    public IReadOnlyList<string> TemporaryDirectories { get; init; } = Array.Empty<string>();
}
