using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TodoDesktopApp.Models;

namespace TodoDesktopApp.Services;

public sealed class TaskExchangeService
{
    private const string PackageManifestName = "tasks.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly TodoService _todoService;

    public TaskExchangeService(TodoService todoService)
    {
        _todoService = todoService;
    }

    public IReadOnlyList<TodoItem> GetAllExportCandidates(bool includeDeleted)
    {
        var todos = _todoService.Search(new TodoSearchCriteria { IncludeNoDue = true });
        return includeDeleted
            ? todos
            : todos.Where(todo => todo.Status != TodoStatus.Deleted).ToList();
    }

    public IReadOnlyList<TodoItem> GetSelectedExportCandidates(IReadOnlyCollection<TodoItem> selectedTodos)
    {
        if (selectedTodos.Count == 0)
        {
            return Array.Empty<TodoItem>();
        }

        var allTodos = _todoService.Search(new TodoSearchCriteria { IncludeNoDue = true });
        var childrenByParentId = allTodos
            .Where(todo => todo.IsSubtask && !string.IsNullOrWhiteSpace(todo.ParentId))
            .GroupBy(todo => todo.ParentId!)
            .ToDictionary(group => group.Key, group => group.ToList());
        var exportIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var todo in selectedTodos)
        {
            AddWithSubtasks(todo.Id, exportIds, childrenByParentId);
        }

        return allTodos.Where(todo => exportIds.Contains(todo.Id)).ToList();
    }

    public bool HasExportAttachments(IReadOnlyList<TodoItem> todos)
    {
        return todos.Any(todo => todo.Attachments.Count > 0);
    }

    public void ExportAllTasks(string filePath)
    {
        ExportTasks(filePath, GetAllExportCandidates(includeDeleted: false));
    }

    public void ExportTasks(string filePath, IReadOnlyList<TodoItem> todos)
    {
        var packageFiles = HasExportAttachments(todos)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : null;
        var document = CreateExportDocument(todos, packageFiles);

        if (packageFiles is null)
        {
            WriteJson(filePath, document);
            return;
        }

        WritePackage(filePath, document, packageFiles);
    }

    public string CreateImportSampleJson()
    {
        return JsonSerializer.Serialize(CreateImportSampleDocument(), JsonOptions);
    }

    private static TaskExchangeDocument CreateImportSampleDocument()
    {
        return new TaskExchangeDocument
        {
            Version = 1,
            SourceApp = "JMtodo Import Sample",
            ExportedAt = DateTime.Now.ToString("O"),
            Groups =
            [
                new TaskExchangeGroup
                {
                    Name = "项目跟进",
                    IconKey = "briefcase",
                    Description = "从会议和文档中整理出的跟进事项"
                }
            ],
            Tasks =
            [
                new TaskExchangeTask
                {
                    Title = "整理项目会议纪要",
                    Note = "从文档中提取需要跟进的事项，确认责任人和截止日期。",
                    StartDate = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
                    DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(2)).ToString("yyyy-MM-dd"),
                    GroupName = "项目跟进",
                    Attachments =
                    [
                        new TaskExchangeAttachment
                        {
                            FileName = "会议纪要.docx",
                            Path = @"C:\Users\用户名\Documents\会议纪要.docx"
                        }
                    ],
                    Subtasks =
                    [
                        new TaskExchangeTask
                        {
                            Title = "确认接口调整事项",
                            Note = "只写明确需要执行的动作，不要写泛泛的背景描述。",
                            StartDate = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
                            DueDate = null
                        },
                        new TaskExchangeTask
                        {
                            Title = "同步给设计和后端",
                            StartDate = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
                            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(1)).ToString("yyyy-MM-dd")
                        }
                    ]
                }
            ]
        };
    }

    public ImportPreviewResult ReadImportPreview(string filePath)
    {
        var temporaryDirectories = new List<string>();
        try
        {
            var (document, packageRoot) = ReadImportDocument(filePath, temporaryDirectories);
            if (document.Version != 1)
            {
                throw new InvalidOperationException(LocalizationService.Format("Import.UnsupportedVersionFormat", document.Version));
            }

            var groupsByName = BuildImportGroups(document.Groups);
            var previewTasks = (document.Tasks ?? [])
                .Select(task => ToPreview(task, isSubtask: false, groupsByName, packageRoot))
                .ToList();
            var filtered = FilterDuplicateTasks(previewTasks);
            return new ImportPreviewResult
            {
                RootTasks = filtered.RootTasks,
                SkippedDuplicateCount = filtered.SkippedDuplicateCount,
                TemporaryDirectories = temporaryDirectories
            };
        }
        catch
        {
            CleanupTemporaryDirectories(temporaryDirectories);
            throw;
        }
    }

    public int ImportTasks(IReadOnlyList<ImportTaskPreview> roots)
    {
        var groups = _todoService.GetGroups().ToList();
        var importedCount = 0;

        foreach (var root in roots.Where(task => task.IsSelected))
        {
            var groupId = EnsureGroupId(root, groups);
            var rootDates = GetImportDates(root);
            var createdRoot = _todoService.Create(
                root.Title,
                root.Note,
                rootDates.StartDate,
                rootDates.DueDate,
                groupId,
                ExistingAttachmentPaths(root));
            importedCount++;

            foreach (var subtask in root.Subtasks.Where(task => task.IsSelected))
            {
                var subtaskDates = GetImportDates(subtask);
                _todoService.CreateSubtask(
                    createdRoot.Id,
                    subtask.Title,
                    subtask.Note,
                    subtaskDates.StartDate,
                    subtaskDates.DueDate,
                    ExistingAttachmentPaths(subtask));
                importedCount++;
            }
        }

        return importedCount;
    }

    public static void CleanupTemporaryDirectories(IEnumerable<string> directories)
    {
        foreach (var directory in directories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // 临时导入目录清理失败不影响用户数据导入。
            }
        }
    }

    private TaskExchangeDocument CreateExportDocument(
        IReadOnlyList<TodoItem> todos,
        Dictionary<string, string>? packageFiles)
    {
        return new TaskExchangeDocument
        {
            Version = 1,
            SourceApp = "JMtodo",
            ExportedAt = DateTime.Now.ToString("O"),
            Groups = BuildExportGroups(todos),
            Tasks = BuildExportTasks(todos, packageFiles)
        };
    }

    private List<TaskExchangeGroup> BuildExportGroups(IReadOnlyList<TodoItem> todos)
    {
        var groupsById = _todoService.GetGroups().ToDictionary(group => group.Id);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TaskExchangeGroup>();

        foreach (var todo in todos)
        {
            if (string.IsNullOrWhiteSpace(todo.GroupId) ||
                !groupsById.TryGetValue(todo.GroupId, out var group) ||
                !seenNames.Add(group.Name))
            {
                continue;
            }

            result.Add(new TaskExchangeGroup
            {
                Name = group.Name,
                IconKey = group.IconKey,
                Description = group.Description
            });
        }

        return result;
    }

    private static List<TaskExchangeTask> BuildExportTasks(
        IReadOnlyList<TodoItem> todos,
        Dictionary<string, string>? packageFiles)
    {
        var exportIds = todos.Select(todo => todo.Id).ToHashSet(StringComparer.Ordinal);
        var childrenByParentId = todos
            .Where(todo => todo.IsSubtask &&
                           !string.IsNullOrWhiteSpace(todo.ParentId) &&
                           exportIds.Contains(todo.ParentId))
            .GroupBy(todo => todo.ParentId!)
            .ToDictionary(group => group.Key, group => group.ToList());

        return todos
            .Where(todo => !todo.IsSubtask ||
                           string.IsNullOrWhiteSpace(todo.ParentId) ||
                           !exportIds.Contains(todo.ParentId))
            .Select(todo => ToExchangeTask(todo, childrenByParentId, packageFiles))
            .ToList();
    }

    private static TaskExchangeTask ToExchangeTask(
        TodoItem todo,
        IReadOnlyDictionary<string, List<TodoItem>> childrenByParentId,
        Dictionary<string, string>? packageFiles)
    {
        return new TaskExchangeTask
        {
            Title = todo.Title,
            Note = todo.Note,
            StartDate = todo.StartDate.ToString("yyyy-MM-dd"),
            DueDate = todo.DueDate?.ToString("yyyy-MM-dd"),
            Status = todo.Status.ToString(),
            GroupName = todo.GroupName,
            Attachments = todo.Attachments.Select(attachment => ToExchangeAttachment(attachment, packageFiles)).ToList(),
            Subtasks = childrenByParentId.TryGetValue(todo.Id, out var children)
                ? children.Select(child => ToExchangeTask(child, childrenByParentId, packageFiles)).ToList()
                : new List<TaskExchangeTask>()
        };
    }

    private static TaskExchangeAttachment ToExchangeAttachment(
        TodoAttachment attachment,
        Dictionary<string, string>? packageFiles)
    {
        var result = new TaskExchangeAttachment
        {
            FileName = attachment.DisplayFileName,
            Path = attachment.FullPath,
            Size = attachment.FileSize
        };

        if (packageFiles is not null && AttachmentFileExists(attachment))
        {
            var packagePath = CreateUniquePackagePath(attachment, packageFiles);
            packageFiles[packagePath] = attachment.FullPath;
            result.PackagePath = packagePath;
        }

        return result;
    }

    private static ImportTaskPreview ToPreview(
        TaskExchangeTask task,
        bool isSubtask,
        IReadOnlyDictionary<string, TaskExchangeGroup> groupsByName,
        string? packageRoot)
    {
        var title = NormalizeRequiredText(task.Title, 80, LocalizationService.Text("Validation.TaskTitleRequired"));
        var note = NormalizeOptionalText(task.Note, 2000);
        var startDate = ParseDateOrToday(task.StartDate);
        var dueDate = ParseOptionalDate(task.DueDate);
        if (dueDate.HasValue && dueDate.Value < startDate)
        {
            throw new InvalidOperationException(LocalizationService.Text("Validation.DueBeforeStart"));
        }

        var groupName = isSubtask ? LocalizationService.Text("Import.FollowParentGroup") : NormalizeOptionalText(task.GroupName, 20);
        var preview = new ImportTaskPreview
        {
            Title = title,
            Note = note,
            StartDate = startDate,
            DueDate = dueDate,
            GroupName = groupName,
            IsSubtask = isSubtask
        };

        if (!isSubtask &&
            !string.IsNullOrWhiteSpace(groupName) &&
            groupsByName.TryGetValue(groupName, out var group))
        {
            preview.GroupIconKey = string.IsNullOrWhiteSpace(group.IconKey) ? "folder" : group.IconKey.Trim();
            preview.GroupDescription = NormalizeOptionalText(group.Description, 100);
        }

        foreach (var attachment in (task.Attachments ?? []).Take(TodoService.MaxAttachmentCount))
        {
            preview.Attachments.Add(new ImportAttachmentPreview
            {
                FileName = NormalizeAttachmentName(attachment),
                Path = ResolveAttachmentPath(attachment, packageRoot)
            });
        }

        if (!isSubtask)
        {
            foreach (var subtask in task.Subtasks ?? [])
            {
                preview.Subtasks.Add(ToPreview(subtask, isSubtask: true, groupsByName, packageRoot));
            }
        }

        return preview;
    }

    private ImportPreviewResult FilterDuplicateTasks(IReadOnlyList<ImportTaskPreview> rootTasks)
    {
        var seenTitles = _todoService.Search(new TodoSearchCriteria { IncludeNoDue = true })
            .Select(todo => NormalizeTitleKey(todo.Title))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredRoots = new List<ImportTaskPreview>();
        var skippedCount = 0;

        foreach (var root in rootTasks)
        {
            if (!seenTitles.Add(NormalizeTitleKey(root.Title)))
            {
                skippedCount += CountTasks(root);
                continue;
            }

            var filteredRoot = ClonePreviewWithoutSubtasks(root);
            foreach (var subtask in root.Subtasks)
            {
                if (!seenTitles.Add(NormalizeTitleKey(subtask.Title)))
                {
                    skippedCount += CountTasks(subtask);
                    continue;
                }

                filteredRoot.Subtasks.Add(ClonePreviewWithoutSubtasks(subtask));
            }

            filteredRoots.Add(filteredRoot);
        }

        return new ImportPreviewResult
        {
            RootTasks = filteredRoots,
            SkippedDuplicateCount = skippedCount
        };
    }

    private static ImportTaskPreview ClonePreviewWithoutSubtasks(ImportTaskPreview source)
    {
        var clone = new ImportTaskPreview
        {
            Title = source.Title,
            Note = source.Note,
            StartDate = source.StartDate,
            DueDate = source.DueDate,
            GroupName = source.GroupName,
            GroupIconKey = source.GroupIconKey,
            GroupDescription = source.GroupDescription,
            IsSelected = source.IsSelected,
            IsSubtask = source.IsSubtask
        };

        foreach (var attachment in source.Attachments)
        {
            clone.Attachments.Add(attachment);
        }

        return clone;
    }

    private static int CountTasks(ImportTaskPreview task)
    {
        return 1 + task.Subtasks.Sum(CountTasks);
    }

    private static string NormalizeTitleKey(string? title)
    {
        return title?.Trim() ?? string.Empty;
    }

    private string? EnsureGroupId(ImportTaskPreview task, List<TodoGroup> groups)
    {
        if (string.IsNullOrWhiteSpace(task.GroupName))
        {
            return null;
        }

        var groupName = task.GroupName.Trim();
        var existing = groups.FirstOrDefault(group =>
            string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = _todoService.CreateGroup(groupName, task.GroupIconKey, task.GroupDescription);
        groups.Add(created);
        return created.Id;
    }

    private static (DateOnly StartDate, DateOnly? DueDate) GetImportDates(ImportTaskPreview task)
    {
        var startDate = ParseRequiredImportDate(task.StartDateInput);
        var dueDate = ParseOptionalImportDate(task.DueDateInput);
        if (dueDate.HasValue && dueDate.Value < startDate)
        {
            throw new InvalidOperationException(LocalizationService.Text("Validation.DueBeforeStart"));
        }

        return (startDate, dueDate);
    }

    private static IEnumerable<string> ExistingAttachmentPaths(ImportTaskPreview task)
    {
        return task.Attachments
            .Where(attachment => attachment.Exists && !string.IsNullOrWhiteSpace(attachment.Path))
            .Select(attachment => attachment.Path!);
    }

    private static (TaskExchangeDocument Document, string? PackageRoot) ReadImportDocument(
        string filePath,
        List<string> temporaryDirectories)
    {
        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var packageRoot = ExtractPackage(filePath, temporaryDirectories);
            var manifestPath = FindPackageManifest(packageRoot);
            return (ReadJson(manifestPath), packageRoot);
        }

        return (ReadJson(filePath), Path.GetDirectoryName(Path.GetFullPath(filePath)));
    }

    private static TaskExchangeDocument ReadJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<TaskExchangeDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException(LocalizationService.Text("Import.InvalidFile"));
    }

    private static string ExtractPackage(string filePath, List<string> temporaryDirectories)
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), $"JMtodo-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packageRoot);
        temporaryDirectories.Add(packageRoot);

        using var archive = ZipFile.OpenRead(filePath);
        var packageRootFullPath = Path.GetFullPath(packageRoot);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var destinationPath = GetSafePackagePath(packageRootFullPath, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        return packageRoot;
    }

    private static string FindPackageManifest(string packageRoot)
    {
        var manifestPath = Path.Combine(packageRoot, PackageManifestName);
        if (File.Exists(manifestPath))
        {
            return manifestPath;
        }

        var jsonFiles = Directory.GetFiles(packageRoot, "*.json", SearchOption.AllDirectories);
        if (jsonFiles.Length == 1)
        {
            return jsonFiles[0];
        }

        throw new InvalidOperationException(LocalizationService.Text("Import.PackageManifestMissing"));
    }

    private static IReadOnlyDictionary<string, TaskExchangeGroup> BuildImportGroups(IEnumerable<TaskExchangeGroup>? groups)
    {
        var result = new Dictionary<string, TaskExchangeGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups ?? [])
        {
            var name = NormalizeOptionalText(group.Name, 20);
            if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name))
            {
                continue;
            }

            result[name] = new TaskExchangeGroup
            {
                Name = name,
                IconKey = string.IsNullOrWhiteSpace(group.IconKey) ? "folder" : group.IconKey.Trim(),
                Description = NormalizeOptionalText(group.Description, 100)
            };
        }

        return result;
    }

    private static string? ResolveAttachmentPath(TaskExchangeAttachment attachment, string? packageRoot)
    {
        if (!string.IsNullOrWhiteSpace(attachment.PackagePath) && !string.IsNullOrWhiteSpace(packageRoot))
        {
            return GetSafePackagePath(Path.GetFullPath(packageRoot), attachment.PackagePath.Trim());
        }

        if (string.IsNullOrWhiteSpace(attachment.Path))
        {
            return null;
        }

        var path = attachment.Path.Trim();
        if (Path.IsPathFullyQualified(path) || string.IsNullOrWhiteSpace(packageRoot))
        {
            return path;
        }

        return GetSafePackagePath(Path.GetFullPath(packageRoot), path);
    }

    private static string GetSafePackagePath(string rootFullPath, string relativePath)
    {
        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootFullPath, normalizedRelativePath));
        if (!fullPath.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(LocalizationService.Text("Import.InvalidFile"));
        }

        return fullPath;
    }

    private static void AddWithSubtasks(
        string todoId,
        HashSet<string> exportIds,
        IReadOnlyDictionary<string, List<TodoItem>> childrenByParentId)
    {
        if (!exportIds.Add(todoId))
        {
            return;
        }

        if (!childrenByParentId.TryGetValue(todoId, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            AddWithSubtasks(child.Id, exportIds, childrenByParentId);
        }
    }

    private static bool AttachmentFileExists(TodoAttachment attachment)
    {
        return !string.IsNullOrWhiteSpace(attachment.FullPath) && File.Exists(attachment.FullPath);
    }

    private static string CreateUniquePackagePath(
        TodoAttachment attachment,
        IReadOnlyDictionary<string, string> packageFiles)
    {
        var taskSegment = SanitizePackageSegment(attachment.TodoId);
        var fileName = SanitizePackageSegment(
            string.IsNullOrWhiteSpace(attachment.StoredFileName)
                ? attachment.DisplayFileName
                : attachment.StoredFileName);
        var basePath = $"attachments/{taskSegment}/{fileName}";
        if (!packageFiles.ContainsKey(basePath))
        {
            return basePath;
        }

        var extension = Path.GetExtension(fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var index = 2;
        string packagePath;
        do
        {
            packagePath = $"attachments/{taskSegment}/{name}-{index}{extension}";
            index++;
        }
        while (packageFiles.ContainsKey(packagePath));

        return packagePath;
    }

    private static string SanitizePackageSegment(string? value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string((value ?? string.Empty)
            .Select(character => invalidChars.Contains(character) || character is '/' or '\\' ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized;
    }

    private static DateOnly ParseDateOrToday(string? value)
    {
        return DateOnly.TryParse(value, out var date)
            ? date
            : DateOnly.FromDateTime(DateTime.Now);
    }

    private static DateOnly? ParseOptionalDate(string? value)
    {
        return DateOnly.TryParse(value, out var date) ? date : null;
    }

    private static DateOnly ParseRequiredImportDate(string? value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidOperationException(LocalizationService.Text("Validation.ImportStartDateInvalid"));
        }

        return date;
    }

    private static DateOnly? ParseOptionalImportDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidOperationException(LocalizationService.Text("Validation.ImportDueDateInvalid"));
        }

        return date;
    }

    private static string NormalizeRequiredText(string? value, int maxLength, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeAttachmentName(TaskExchangeAttachment attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.FileName))
        {
            return attachment.FileName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attachment.Path))
        {
            return Path.GetFileName(attachment.Path);
        }

        return string.IsNullOrWhiteSpace(attachment.PackagePath)
            ? LocalizationService.Text("Import.UnknownFile")
            : Path.GetFileName(attachment.PackagePath);
    }

    private static void WriteJson(string filePath, TaskExchangeDocument document)
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(document, JsonOptions));
    }

    private static void WritePackage(
        string filePath,
        TaskExchangeDocument document,
        IReadOnlyDictionary<string, string> packageFiles)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry(PackageManifestName, CompressionLevel.Optimal);
        using (var stream = manifest.Open())
        {
            JsonSerializer.Serialize(stream, document, JsonOptions);
        }

        foreach (var (packagePath, sourcePath) in packageFiles)
        {
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var entry = archive.CreateEntry(packagePath, CompressionLevel.Optimal);
            using var source = File.OpenRead(sourcePath);
            using var destination = entry.Open();
            source.CopyTo(destination);
        }
    }
}
