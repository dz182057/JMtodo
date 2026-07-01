using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TodoDesktopApp.Models;

namespace TodoDesktopApp.Services;

public sealed class TaskExchangeService
{
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

    public void ExportAllTasks(string filePath)
    {
        var todos = _todoService.Search(new TodoSearchCriteria { IncludeNoDue = true })
            .Where(todo => todo.Status != TodoStatus.Deleted)
            .ToList();
        var document = new TaskExchangeDocument
        {
            Version = 1,
            SourceApp = "JMtodo",
            ExportedAt = DateTime.Now.ToString("O"),
            Tasks = BuildExportTasks(todos)
        };

        WriteJson(filePath, document);
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
        var json = File.ReadAllText(filePath);
        var document = JsonSerializer.Deserialize<TaskExchangeDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException(LocalizationService.Text("Import.InvalidFile"));
        if (document.Version != 1)
        {
            throw new InvalidOperationException(LocalizationService.Format("Import.UnsupportedVersionFormat", document.Version));
        }

        var previewTasks = (document.Tasks ?? []).Select(task => ToPreview(task, isSubtask: false)).ToList();
        return FilterDuplicateTasks(previewTasks);
    }

    public int ImportTasks(IReadOnlyList<ImportTaskPreview> roots)
    {
        var groups = _todoService.GetGroups().ToList();
        var importedCount = 0;

        foreach (var root in roots)
        {
            var groupId = EnsureGroupId(root.GroupName, groups);
            var createdRoot = _todoService.Create(
                root.Title,
                root.Note,
                root.StartDate,
                root.DueDate,
                groupId,
                ExistingAttachmentPaths(root));
            importedCount++;

            foreach (var subtask in root.Subtasks)
            {
                _todoService.CreateSubtask(
                    createdRoot.Id,
                    subtask.Title,
                    subtask.Note,
                    subtask.StartDate,
                    subtask.DueDate,
                    ExistingAttachmentPaths(subtask));
                importedCount++;
            }
        }

        return importedCount;
    }

    private static List<TaskExchangeTask> BuildExportTasks(IReadOnlyList<TodoItem> todos)
    {
        var childrenByParentId = todos
            .Where(todo => todo.IsSubtask && !string.IsNullOrWhiteSpace(todo.ParentId))
            .GroupBy(todo => todo.ParentId!)
            .ToDictionary(group => group.Key, group => group.ToList());

        return todos
            .Where(todo => !todo.IsSubtask)
            .Select(todo => ToExchangeTask(todo, childrenByParentId))
            .ToList();
    }

    private static TaskExchangeTask ToExchangeTask(
        TodoItem todo,
        IReadOnlyDictionary<string, List<TodoItem>> childrenByParentId)
    {
        return new TaskExchangeTask
        {
            Title = todo.Title,
            Note = todo.Note,
            StartDate = todo.StartDate.ToString("yyyy-MM-dd"),
            DueDate = todo.DueDate?.ToString("yyyy-MM-dd"),
            Status = todo.Status.ToString(),
            GroupName = todo.GroupName,
            Attachments = todo.Attachments.Select(ToExchangeAttachment).ToList(),
            Subtasks = childrenByParentId.TryGetValue(todo.Id, out var children)
                ? children.Select(child => ToExchangeTask(child, childrenByParentId)).ToList()
                : new List<TaskExchangeTask>()
        };
    }

    private static TaskExchangeAttachment ToExchangeAttachment(TodoAttachment attachment)
    {
        return new TaskExchangeAttachment
        {
            FileName = attachment.DisplayFileName,
            Path = attachment.FullPath,
            Size = attachment.FileSize
        };
    }

    private static ImportTaskPreview ToPreview(TaskExchangeTask task, bool isSubtask)
    {
        var title = NormalizeRequiredText(task.Title, 80, LocalizationService.Text("Validation.TaskTitleRequired"));
        var note = NormalizeOptionalText(task.Note, 500);
        var startDate = ParseDateOrToday(task.StartDate);
        var dueDate = ParseOptionalDate(task.DueDate);
        if (dueDate.HasValue && dueDate.Value < startDate)
        {
            throw new InvalidOperationException(LocalizationService.Text("Validation.DueBeforeStart"));
        }

        var preview = new ImportTaskPreview
        {
            Title = title,
            Note = note,
            StartDate = startDate,
            DueDate = dueDate,
            GroupName = isSubtask ? null : NormalizeOptionalText(task.GroupName, 20)
        };

        foreach (var attachment in (task.Attachments ?? []).Take(TodoService.MaxAttachmentCount))
        {
            preview.Attachments.Add(new ImportAttachmentPreview
            {
                FileName = NormalizeAttachmentName(attachment),
                Path = string.IsNullOrWhiteSpace(attachment.Path) ? null : attachment.Path.Trim()
            });
        }

        if (!isSubtask)
        {
            foreach (var subtask in task.Subtasks ?? [])
            {
                preview.Subtasks.Add(ToPreview(subtask, isSubtask: true));
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
            GroupName = source.GroupName
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

    private string? EnsureGroupId(string? groupName, List<TodoGroup> groups)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return null;
        }

        var existing = groups.FirstOrDefault(group =>
            string.Equals(group.Name, groupName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = _todoService.CreateGroup(groupName.Trim());
        groups.Add(created);
        return created.Id;
    }

    private static IEnumerable<string> ExistingAttachmentPaths(ImportTaskPreview task)
    {
        return task.Attachments
            .Where(attachment => attachment.Exists && !string.IsNullOrWhiteSpace(attachment.Path))
            .Select(attachment => attachment.Path!);
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

        return string.IsNullOrWhiteSpace(attachment.Path)
            ? LocalizationService.Text("Import.UnknownFile")
            : Path.GetFileName(attachment.Path);
    }

    private static void WriteJson(string filePath, TaskExchangeDocument document)
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(document, JsonOptions));
    }
}
