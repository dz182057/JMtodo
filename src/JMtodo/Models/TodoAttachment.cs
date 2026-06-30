using System.IO;
using Brush = System.Windows.Media.Brush;

namespace TodoDesktopApp.Models;

public sealed class TodoAttachment
{
    public string Id { get; set; } = string.Empty;

    public string TodoId { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public DateTime CreatedAt { get; set; }

    public string DisplayFileName => string.IsNullOrWhiteSpace(OriginalFileName)
        ? StoredFileName
        : OriginalFileName;

    public string FileSizeText => FormatFileSize(FileSize);

    public string FileTypeLabel => FileTypeIcon.Label;

    public Brush FileTypeForeground => FileTypeIcon.Foreground;

    public Brush FileTypeBackground => FileTypeIcon.Background;

    private AttachmentFileTypeIcon FileTypeIcon => AttachmentFileTypeIconCatalog.Get(DisplayFileName);

    public TodoAttachment Clone()
    {
        return new TodoAttachment
        {
            Id = Id,
            TodoId = TodoId,
            OriginalFileName = OriginalFileName,
            StoredFileName = StoredFileName,
            RelativePath = RelativePath,
            FullPath = FullPath,
            FileSize = FileSize,
            CreatedAt = CreatedAt
        };
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        var mb = kb / 1024d;
        if (mb < 1024)
        {
            return $"{mb:0.#} MB";
        }

        var gb = mb / 1024d;
        return $"{gb:0.#} GB";
    }
}
