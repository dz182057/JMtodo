using System.Collections.ObjectModel;
using System.IO;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;
using Brush = System.Windows.Media.Brush;

namespace TodoDesktopApp.ViewModels;

public sealed class TodoEditorViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private string? _note;
    private DateTime? _startDate = DateTime.Today;
    private DateTime? _dueDate;
    private bool _isNoDue = true;
    private string? _titleError;
    private string? _errorMessage;
    private TodoGroupOption? _selectedGroup;
    private bool _showTitleError;
    private bool _isTitleValid;

    public TodoEditorViewModel(IEnumerable<TodoGroup>? groups = null)
    {
        LoadGroupOptions(groups, null);
        UpdateValidationState();
    }

    public TodoEditorViewModel(TodoItem item)
    {
        Id = item.Id;
        ParentId = item.ParentId;
        ParentTitle = item.ParentTitle;
        IsSubtask = item.IsSubtask;
        Title = item.Title;
        Note = item.Note;
        StartDate = item.StartDate.ToDateTime(TimeOnly.MinValue);
        DueDate = item.DueDate?.ToDateTime(TimeOnly.MinValue);
        IsNoDue = !item.DueDate.HasValue;
        LoadGroupOptions(null, item.GroupId);
        LoadAttachments(item.Attachments);
        UpdateValidationState();
    }

    public TodoEditorViewModel(TodoItem parent, bool isNewSubtask)
    {
        if (!isNewSubtask)
        {
            throw new InvalidOperationException("子任务编辑器参数无效。");
        }

        ParentId = parent.Id;
        ParentTitle = parent.Title;
        IsSubtask = true;
        LoadGroupOptions(null, parent.GroupId);
        UpdateValidationState();
    }

    public string? Id { get; }

    public string? ParentId { get; }

    public string? ParentTitle { get; }

    public bool IsSubtask { get; }

    public bool IsGroupSelectorVisible => string.IsNullOrWhiteSpace(Id) && !IsSubtask;

    public bool HasAttachments => Attachments.Count > 0;

    public bool CanAddAttachment => Attachments.Count < TodoService.MaxAttachmentCount;

    public string AttachmentCountText => $"{Attachments.Count}/{TodoService.MaxAttachmentCount}";

    public ObservableCollection<TodoGroupOption> GroupOptions { get; } = new();

    public ObservableCollection<TodoEditorAttachmentItem> Attachments { get; } = new();

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                UpdateValidationState();
            }
        }
    }

    public string? Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value))
            {
                UpdateValidationState();
            }
        }
    }

    public DateTime? StartDate
    {
        get => _startDate;
        set
        {
            if (SetProperty(ref _startDate, value))
            {
                UpdateValidationState();
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (SetProperty(ref _dueDate, value))
            {
                if (value.HasValue && IsNoDue)
                {
                    _isNoDue = false;
                    OnPropertyChanged(nameof(IsNoDue));
                    OnPropertyChanged(nameof(IsDueDateEnabled));
                }

                UpdateValidationState();
            }
        }
    }

    public bool IsNoDue
    {
        get => _isNoDue;
        set
        {
            if (SetProperty(ref _isNoDue, value))
            {
                if (value)
                {
                    DueDate = null;
                }

                OnPropertyChanged(nameof(IsDueDateEnabled));
                UpdateValidationState();
            }
        }
    }

    public bool IsDueDateEnabled => !IsNoDue;

    public TodoGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public string? SelectedGroupId => SelectedGroup?.Id;

    public string? TitleError
    {
        get => _titleError;
        private set => SetProperty(ref _titleError, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(InfoBarText));
            }
        }
    }

    public string InfoBarText => ErrorMessage ?? (IsSubtask
        ? $"子任务将添加到「{ParentTitle}」下，暂时只支持二级子任务。"
        : "新任务默认状态为未完成，可在主列表或悬浮窗中快速标记完成。");

    public bool CanSave =>
        _isTitleValid &&
        string.IsNullOrWhiteSpace(ErrorMessage);

    public bool Validate()
    {
        _showTitleError = true;
        UpdateValidationState();

        return CanSave;
    }

    public void MarkTitleTouched()
    {
        _showTitleError = true;
        UpdateValidationState();
    }

    public void AddAttachmentFiles(IEnumerable<string> filePaths)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => Attachments.All(attachment =>
                !attachment.IsNew ||
                !string.Equals(attachment.OpenPath, path, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }

        if (Attachments.Count + paths.Count > TodoService.MaxAttachmentCount)
        {
            throw new InvalidOperationException($"一个任务最多关联 {TodoService.MaxAttachmentCount} 个文件。");
        }

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"选择的文件不存在：{Path.GetFileName(path)}");
            }

            Attachments.Add(TodoEditorAttachmentItem.FromFile(path));
        }

        NotifyAttachmentStateChanged();
    }

    public void RemoveAttachment(TodoEditorAttachmentItem attachment)
    {
        if (Attachments.Remove(attachment))
        {
            NotifyAttachmentStateChanged();
        }
    }

    public DateOnly GetStartDate() => DateOnly.FromDateTime(StartDate!.Value);

    public DateOnly? GetDueDate() => IsNoDue || !DueDate.HasValue ? null : DateOnly.FromDateTime(DueDate.Value);

    public IReadOnlyCollection<string> GetKeptAttachmentIds()
    {
        return Attachments
            .Where(attachment => !attachment.IsNew && !string.IsNullOrWhiteSpace(attachment.Id))
            .Select(attachment => attachment.Id!)
            .ToList();
    }

    public IReadOnlyList<string> GetNewAttachmentPaths()
    {
        return Attachments
            .Where(attachment => attachment.IsNew)
            .Select(attachment => attachment.OpenPath)
            .ToList();
    }

    public string AttachmentStateSignature => string.Join("|", Attachments.Select(attachment => attachment.StateKey));

    private void LoadGroupOptions(IEnumerable<TodoGroup>? groups, string? selectedGroupId)
    {
        GroupOptions.Clear();
        var noGroup = new TodoGroupOption { Name = "不加入任务组" };
        GroupOptions.Add(noGroup);

        if (groups is not null)
        {
            foreach (var group in groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase))
            {
                GroupOptions.Add(new TodoGroupOption { Id = group.Id, Name = group.Name });
            }
        }

        SelectedGroup = GroupOptions.FirstOrDefault(group => group.Id == selectedGroupId) ?? noGroup;
    }

    private void LoadAttachments(IEnumerable<TodoAttachment> attachments)
    {
        Attachments.Clear();
        foreach (var attachment in attachments)
        {
            Attachments.Add(TodoEditorAttachmentItem.FromAttachment(attachment));
        }
    }

    private void NotifyAttachmentStateChanged()
    {
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(CanAddAttachment));
        OnPropertyChanged(nameof(AttachmentCountText));
        OnPropertyChanged(nameof(AttachmentStateSignature));
    }

    private void UpdateValidationState()
    {
        var titleError = string.IsNullOrWhiteSpace(Title) ? "请输入任务标题" : null;

        if (Title?.Length > 80)
        {
            titleError = "标题不能超过 80 字";
        }

        _isTitleValid = titleError is null;
        TitleError = _showTitleError ? titleError : null;

        if (Note?.Length > 500)
        {
            ErrorMessage = "备注不能超过 500 字";
        }
        else if (!StartDate.HasValue)
        {
            ErrorMessage = "请选择起始日期";
        }
        else if (!IsNoDue && DueDate.HasValue && DueDate.Value.Date < StartDate.Value.Date)
        {
            ErrorMessage = "计划完成日期不能早于开始日期";
        }
        else
        {
            ErrorMessage = null;
        }

        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(InfoBarText));
    }
}

public sealed class TodoGroupOption
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class TodoEditorAttachmentItem
{
    private TodoEditorAttachmentItem(
        string? id,
        string displayFileName,
        string openPath,
        long fileSize,
        bool isNew)
    {
        Id = id;
        DisplayFileName = displayFileName;
        OpenPath = openPath;
        FileSize = fileSize;
        IsNew = isNew;
    }

    public string? Id { get; }

    public string DisplayFileName { get; }

    public string OpenPath { get; }

    public long FileSize { get; }

    public bool IsNew { get; }

    public string FileSizeText => FormatFileSize(FileSize);

    public string FileTypeLabel => FileTypeIcon.Label;

    public Brush FileTypeForeground => FileTypeIcon.Foreground;

    public Brush FileTypeBackground => FileTypeIcon.Background;

    public string StateKey => IsNew ? $"new:{OpenPath}" : $"existing:{Id}";

    private AttachmentFileTypeIcon FileTypeIcon => AttachmentFileTypeIconCatalog.Get(DisplayFileName);

    public static TodoEditorAttachmentItem FromAttachment(TodoAttachment attachment)
    {
        return new TodoEditorAttachmentItem(
            attachment.Id,
            attachment.DisplayFileName,
            attachment.FullPath,
            attachment.FileSize,
            isNew: false);
    }

    public static TodoEditorAttachmentItem FromFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return new TodoEditorAttachmentItem(
            id: null,
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length,
            isNew: true);
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
