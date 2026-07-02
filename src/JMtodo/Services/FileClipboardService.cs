using System.Collections.Specialized;
using System.IO;

namespace TodoDesktopApp.Services;

public static class FileClipboardService
{
    public static void CopyFile(string filePath)
    {
        CopyFiles([filePath]);
    }

    public static void CopyFiles(IEnumerable<string> filePaths)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0 || paths.Any(path => !File.Exists(path)))
        {
            throw new InvalidOperationException(LocalizationService.Text("Dialog.CopyFileFailed.Missing"));
        }

        var collection = new StringCollection();
        collection.AddRange(paths.ToArray());
        System.Windows.Clipboard.SetFileDropList(collection);
    }
}
