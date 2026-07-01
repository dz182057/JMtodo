using System.IO;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace TodoDesktopApp.Models;

public sealed class AttachmentFileTypeIcon
{
    public string Label { get; init; } = "FILE";

    public Brush Foreground { get; init; } = Brushes.Gray;

    public Brush Background { get; init; } = Brushes.Transparent;
}

public static class AttachmentFileTypeIconCatalog
{
    private static readonly AttachmentFileTypeIcon Word = Create("DOC", "#2563EB", "#EFF6FF");
    private static readonly AttachmentFileTypeIcon Excel = Create("XLS", "#16A34A", "#F0FDF4");
    private static readonly AttachmentFileTypeIcon PowerPoint = Create("PPT", "#EA580C", "#FFF7ED");
    private static readonly AttachmentFileTypeIcon Pdf = Create("PDF", "#DC2626", "#FEF2F2");
    private static readonly AttachmentFileTypeIcon Image = Create("IMG", "#7C3AED", "#F5F3FF");
    private static readonly AttachmentFileTypeIcon Archive = Create("ZIP", "#D97706", "#FFFBEB");
    private static readonly AttachmentFileTypeIcon Text = Create("TXT", "#475569", "#F8FAFC");
    private static readonly AttachmentFileTypeIcon Code = Create("CODE", "#0891B2", "#ECFEFF");
    private static readonly AttachmentFileTypeIcon Video = Create("VID", "#DB2777", "#FDF2F8");
    private static readonly AttachmentFileTypeIcon Audio = Create("AUD", "#9333EA", "#FAF5FF");
    private static readonly AttachmentFileTypeIcon Database = Create("DB", "#0F766E", "#F0FDFA");
    private static readonly AttachmentFileTypeIcon App = Create("APP", "#4F46E5", "#EEF2FF");
    private static readonly AttachmentFileTypeIcon Default = Create("FILE", "#3F7BFF", "#EEF5FF");
    private static readonly AttachmentFileTypeIcon Multiple = Create("ALL", "#3F7BFF", "#EEF5FF");

    public static AttachmentFileTypeIcon Get(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".doc" or ".docx" or ".wps" => Word,
            ".xls" or ".xlsx" or ".csv" or ".et" => Excel,
            ".ppt" or ".pptx" or ".dps" => PowerPoint,
            ".pdf" => Pdf,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => Image,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => Archive,
            ".txt" or ".md" or ".log" or ".ini" or ".json" or ".xml" or ".yaml" or ".yml" => Text,
            ".cs" or ".xaml" or ".js" or ".ts" or ".tsx" or ".jsx" or ".html" or ".css" or ".scss" or ".py" or ".java" or ".cpp" or ".c" or ".h" or ".sh" or ".ps1" => Code,
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" => Video,
            ".mp3" or ".wav" or ".flac" or ".aac" => Audio,
            ".db" or ".sqlite" or ".sql" => Database,
            ".exe" or ".msi" or ".bat" or ".cmd" => App,
            _ => Default
        };
    }

    public static AttachmentFileTypeIcon GetMultiple() => Multiple;

    private static AttachmentFileTypeIcon Create(string label, string foreground, string background)
    {
        return new AttachmentFileTypeIcon
        {
            Label = label,
            Foreground = CreateBrush(foreground),
            Background = CreateBrush(background)
        };
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
