using System.IO;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.Models;

public sealed class ImportAttachmentPreview
{
    public string FileName { get; set; } = string.Empty;

    public string? Path { get; set; }

    public bool Exists => !string.IsNullOrWhiteSpace(Path) && File.Exists(Path);

    public string StatusText => Exists
        ? LocalizationService.Text("Import.AttachmentReady")
        : LocalizationService.Text("Import.AttachmentMissing");
}
