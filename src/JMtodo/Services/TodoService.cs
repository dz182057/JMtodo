using TodoDesktopApp.Data;
using TodoDesktopApp.Models;
using System.Diagnostics;
using System.IO;

namespace TodoDesktopApp.Services;

public sealed class TodoService
{
    public const int MaxAttachmentCount = 10;

    private readonly TodoRepository _repository;

    public TodoService(TodoRepository repository)
    {
        _repository = repository;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<TodoGroup> GetGroups() => _repository.GetGroups();

    public IReadOnlyList<TodoItem> Search(TodoSearchCriteria criteria) => _repository.Search(criteria);

    public IReadOnlyList<TodoItem> GetCurrentActiveTodos()
    {
        return _repository.GetCurrentActive(DateOnly.FromDateTime(DateTime.Now));
    }

    public IReadOnlyList<TodoItem> GetFloatingTodos()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todos = Search(new TodoSearchCriteria { Status = TodoStatus.Active, IncludeNoDue = true })
            .Where(todo => todo.StartDate <= today)
            .ToList();

        var result = new List<TodoItem>();
        var roots = OrderBySortThenDefault(todos.Where(todo => !todo.IsSubtask)).ToList();

        foreach (var root in roots)
        {
            result.Add(root);

            result.AddRange(OrderBySortThenDefault(todos.Where(todo => todo.ParentId == root.Id)));
        }

        result.AddRange(OrderBySortThenDefault(todos
            .Where(todo => todo.IsSubtask && roots.All(root => root.Id != todo.ParentId))));

        return result;
    }

    public TodoGroup CreateGroup(string name, string iconKey = "folder", string? description = null)
    {
        var trimmedName = ValidateGroupName(name);
        var trimmedDescription = NormalizeGroupDescription(description);

        var existing = _repository.GetGroups()
            .FirstOrDefault(group => string.Equals(group.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            throw new InvalidOperationException(T("Service.GroupDuplicate"));
        }

        var group = new TodoGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmedName,
            IconKey = NormalizeIconKey(iconKey),
            Description = trimmedDescription,
            CreatedAt = DateTime.Now,
            SortOrder = _repository.GetNextGroupSortOrder()
        };

        _repository.InsertGroup(group);
        NotifyChanged();
        return group;
    }

    public void RenameGroup(string id, string name)
    {
        var group = _repository.GetGroups().FirstOrDefault(group => group.Id == id)
            ?? throw new InvalidOperationException(T("Service.GroupNotFoundForUpdate"));
        UpdateGroup(id, name, group.IconKey, group.Description);
    }

    public void UpdateGroup(string id, string name, string iconKey, string? description)
    {
        var trimmedName = ValidateGroupName(name);
        var trimmedDescription = NormalizeGroupDescription(description);
        var groups = _repository.GetGroups();
        var group = groups.FirstOrDefault(group => group.Id == id) ?? throw new InvalidOperationException(T("Service.GroupNotFoundForUpdate"));
        if (groups.Any(item => item.Id != id && string.Equals(item.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(T("Service.GroupDuplicate"));
        }

        group.Name = trimmedName;
        group.IconKey = NormalizeIconKey(iconKey);
        group.Description = trimmedDescription;
        _repository.UpdateGroup(group);
        NotifyChanged();
    }

    public void DeleteGroup(string id)
    {
        if (_repository.GetGroups().All(group => group.Id != id))
        {
            throw new InvalidOperationException(T("Service.GroupNotFoundForDelete"));
        }

        _repository.DeleteGroup(id);
        NotifyChanged();
    }

    public bool TryReorderGroup(string draggedId, string targetId, bool insertBefore)
    {
        if (draggedId == targetId)
        {
            return false;
        }

        var groups = _repository.GetGroups().ToList();
        var dragged = groups.FirstOrDefault(group => group.Id == draggedId);
        var target = groups.FirstOrDefault(group => group.Id == targetId);
        if (dragged is null || target is null)
        {
            return false;
        }

        groups.RemoveAll(group => group.Id == dragged.Id);
        var targetIndex = groups.FindIndex(group => group.Id == target.Id);
        if (targetIndex < 0)
        {
            return false;
        }

        if (!insertBefore)
        {
            targetIndex++;
        }

        groups.Insert(targetIndex, dragged);
        var sortUpdates = groups
            .Select((group, index) => (group.Id, SortOrder: (index + 1) * 10))
            .ToList();
        _repository.UpdateGroupSortOrders(sortUpdates);
        NotifyChanged();
        return true;
    }

    public TodoItem Create(
        string title,
        string? note,
        DateOnly startDate,
        DateOnly? dueDate,
        string? groupId = null,
        IEnumerable<string>? attachmentFilePaths = null,
        bool pinToTop = false)
    {
        return CreateTodo(title, note, startDate, dueDate, parentId: null, groupId, attachmentFilePaths, pinToTop);
    }

    public TodoItem CreateSubtask(
        string parentId,
        string title,
        string? note,
        DateOnly startDate,
        DateOnly? dueDate,
        IEnumerable<string>? attachmentFilePaths = null,
        bool pinToTop = false)
    {
        var parent = _repository.GetById(parentId) ?? throw new InvalidOperationException(T("Service.ParentNotFound"));
        if (parent.IsSubtask)
        {
            throw new InvalidOperationException(T("Service.SubtaskDepthOnly"));
        }

        if (parent.Status == TodoStatus.Deleted)
        {
            throw new InvalidOperationException(T("Service.DeletedCannotAddSubtask"));
        }

        if (parent.Status != TodoStatus.Active)
        {
            throw new InvalidOperationException(T("Service.OnlyActiveRootCanAddSubtask"));
        }

        return CreateTodo(title, note, startDate, dueDate, parent.Id, parent.GroupId, attachmentFilePaths, pinToTop);
    }

    private TodoItem CreateTodo(
        string title,
        string? note,
        DateOnly startDate,
        DateOnly? dueDate,
        string? parentId,
        string? groupId,
        IEnumerable<string>? attachmentFilePaths,
        bool pinToTop)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException(T("Service.TaskTitleRequired"));
        }

        if (!string.IsNullOrWhiteSpace(groupId) && _repository.GetGroups().All(group => group.Id != groupId))
        {
            throw new InvalidOperationException(T("Service.SelectedGroupMissing"));
        }

        var newAttachmentPaths = NormalizeAttachmentPaths(attachmentFilePaths);
        ValidateAttachmentLimit(0, newAttachmentPaths.Count);
        ValidateAttachmentSources(newAttachmentPaths);

        var now = DateTime.Now;
        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentId = parentId,
            GroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId,
            Title = title.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            StartDate = startDate,
            DueDate = dueDate,
            Status = TodoStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = _repository.GetNextActiveSortOrder(parentId)
        };

        _repository.Insert(item);
        AddAttachments(item.Id, newAttachmentPaths);
        if (pinToTop)
        {
            MoveActiveTodoToTop(item);
        }

        NotifyChanged();
        return item;
    }

    public void Update(
        TodoItem item,
        IReadOnlyCollection<string>? keptAttachmentIds = null,
        IEnumerable<string>? newAttachmentFilePaths = null)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            throw new InvalidOperationException(T("Service.TaskTitleRequired"));
        }

        var newAttachmentPaths = NormalizeAttachmentPaths(newAttachmentFilePaths);
        var existing = _repository.GetById(item.Id) ?? throw new InvalidOperationException(T("Service.TaskNotFoundForUpdate"));
        existing.Title = item.Title.Trim();
        existing.Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim();
        existing.StartDate = item.StartDate;
        existing.DueDate = item.DueDate;
        existing.UpdatedAt = DateTime.Now;

        _repository.Update(existing);
        if (keptAttachmentIds is not null || newAttachmentPaths.Count > 0)
        {
            SyncAttachments(existing, keptAttachmentIds, newAttachmentPaths);
        }

        NotifyChanged();
    }

    public void OpenAttachment(TodoAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.FullPath) || !File.Exists(attachment.FullPath))
        {
            throw new InvalidOperationException(T("Service.FileMovedOrDeleted"));
        }

        Process.Start(new ProcessStartInfo(attachment.FullPath) { UseShellExecute = true });
    }

    public void MoveRootTodosToGroup(IEnumerable<string> todoIds, string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || _repository.GetGroups().All(group => group.Id != groupId))
        {
            throw new InvalidOperationException(T("Service.TargetGroupMissing"));
        }

        var selectedIds = todoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (selectedIds.Count == 0)
        {
            throw new InvalidOperationException(T("Service.MoveSelectRoot"));
        }

        var allTodos = _repository.Search(new TodoSearchCriteria { IncludeNoDue = true });
        var selectedRoots = allTodos.Where(todo => selectedIds.Contains(todo.Id)).ToList();
        if (selectedRoots.Count != selectedIds.Count || selectedRoots.Any(todo => todo.IsSubtask))
        {
            throw new InvalidOperationException(T("Service.MoveRootOnly"));
        }

        var now = DateTime.Now;
        foreach (var root in selectedRoots)
        {
            MoveTodoToGroup(root, groupId, now);

            foreach (var subtask in allTodos.Where(todo => todo.ParentId == root.Id))
            {
                MoveTodoToGroup(subtask, groupId, now);
            }
        }

        NotifyChanged();
    }

    public void Complete(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException(T("Service.TaskNotFoundForComplete"));
        item.Status = TodoStatus.Completed;
        item.CompletedAt = DateTime.Now;
        item.DeletedAt = null;
        item.UpdatedAt = DateTime.Now;
        _repository.Update(item);
        NotifyChanged();
    }

    public void Reopen(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException(T("Service.TaskNotFoundForReopen"));
        item.Status = TodoStatus.Active;
        item.CompletedAt = null;
        item.DeletedAt = null;
        item.UpdatedAt = DateTime.Now;
        _repository.Update(item);
        NotifyChanged();
    }

    public void SoftDelete(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException(T("Service.TaskNotFoundForDelete"));
        var now = DateTime.Now;
        SoftDeleteItem(item, now);

        if (!item.IsSubtask)
        {
            foreach (var subtask in _repository.Search(new TodoSearchCriteria { IncludeNoDue = true })
                         .Where(todo => todo.ParentId == item.Id))
            {
                SoftDeleteItem(subtask, now);
            }
        }

        NotifyChanged();
    }

    public void Restore(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException(T("Service.TaskNotFoundForRestore"));
        item.Status = TodoStatus.Active;
        item.DeletedAt = null;
        item.CompletedAt = null;
        item.UpdatedAt = DateTime.Now;
        _repository.Update(item);
        NotifyChanged();
    }

    public void DeletePermanent(string id)
    {
        var item = _repository.GetById(id);
        if (item is not null && !item.IsSubtask)
        {
            foreach (var subtask in _repository.Search(new TodoSearchCriteria { IncludeNoDue = true })
                         .Where(todo => todo.ParentId == item.Id))
            {
                DeleteAttachmentFiles(subtask);
                _repository.DeletePermanent(subtask.Id);
            }
        }

        if (item is not null)
        {
            DeleteAttachmentFiles(item);
        }

        _repository.DeletePermanent(id);
        NotifyChanged();
    }

    private void SoftDeleteItem(TodoItem item, DateTime deletedAt)
    {
        item.Status = TodoStatus.Deleted;
        item.DeletedAt = deletedAt;
        item.UpdatedAt = deletedAt;
        _repository.Update(item);
    }

    private void MoveTodoToGroup(TodoItem item, string groupId, DateTime updatedAt)
    {
        item.GroupId = groupId;
        item.UpdatedAt = updatedAt;
        _repository.Update(item);
    }

    public bool TryReorderActiveTodo(string draggedId, string targetId, bool insertBefore)
    {
        if (draggedId == targetId)
        {
            return false;
        }

        var todos = Search(new TodoSearchCriteria { Status = TodoStatus.Active, IncludeNoDue = true });
        var dragged = todos.FirstOrDefault(todo => todo.Id == draggedId);
        var target = todos.FirstOrDefault(todo => todo.Id == targetId);
        if (dragged is null || target is null)
        {
            return false;
        }

        if (dragged.IsSubtask != target.IsSubtask)
        {
            return false;
        }

        var siblings = dragged.IsSubtask
            ? todos.Where(todo => todo.ParentId == dragged.ParentId && todo.IsSubtask).ToList()
            : todos.Where(todo => !todo.IsSubtask).ToList();
        if (dragged.IsSubtask && dragged.ParentId != target.ParentId)
        {
            return false;
        }

        if (siblings.All(todo => todo.Id != target.Id))
        {
            return false;
        }

        var ordered = OrderBySortThenDefault(siblings).ToList();
        ordered.RemoveAll(todo => todo.Id == dragged.Id);
        var targetIndex = ordered.FindIndex(todo => todo.Id == target.Id);
        if (targetIndex < 0)
        {
            return false;
        }

        if (!insertBefore)
        {
            targetIndex++;
        }

        ordered.Insert(targetIndex, dragged);
        var sortUpdates = ordered
            .Select((todo, index) => (todo.Id, SortOrder: (index + 1) * 10))
            .ToList();
        _repository.UpdateSortOrders(sortUpdates);
        NotifyChanged();
        return true;
    }

    private void MoveActiveTodoToTop(TodoItem item)
    {
        var todos = Search(new TodoSearchCriteria { Status = TodoStatus.Active, IncludeNoDue = true });
        var siblings = item.IsSubtask
            ? todos.Where(todo => todo.ParentId == item.ParentId && todo.IsSubtask)
            : todos.Where(todo => !todo.IsSubtask);

        var ordered = OrderBySortThenDefault(siblings)
            .Where(todo => todo.Id != item.Id)
            .ToList();
        ordered.Insert(0, item);

        var sortUpdates = ordered
            .Select((todo, index) => (todo.Id, SortOrder: (index + 1) * 10))
            .ToList();
        _repository.UpdateSortOrders(sortUpdates);
        item.SortOrder = sortUpdates.First(update => update.Id == item.Id).SortOrder;
    }

    private static string ValidateGroupName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(T("Service.GroupNameRequired"));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 20)
        {
            throw new InvalidOperationException(T("Service.GroupNameMax"));
        }

        return trimmedName;
    }

    private static IOrderedEnumerable<TodoItem> OrderBySortThenDefault(IEnumerable<TodoItem> todos)
    {
        return todos
            .OrderBy(todo => HasSortOrder(todo) ? 0 : 1)
            .ThenBy(todo => HasSortOrder(todo) ? todo.SortOrder : int.MaxValue)
            .ThenBy(todo => todo.DueDate.HasValue ? 0 : 1)
            .ThenBy(todo => todo.DueDate)
            .ThenBy(todo => todo.StartDate)
            .ThenBy(todo => todo.CreatedAt);
    }

    private static bool HasSortOrder(TodoItem todo) => todo.SortOrder > 0;

    private void SyncAttachments(
        TodoItem existing,
        IReadOnlyCollection<string>? keptAttachmentIds,
        IReadOnlyList<string> newAttachmentPaths)
    {
        var existingIds = existing.Attachments.Select(attachment => attachment.Id).ToHashSet(StringComparer.Ordinal);
        var keptIds = keptAttachmentIds is null
            ? existingIds
            : keptAttachmentIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

        if (keptIds.Any(id => !existingIds.Contains(id)))
        {
            throw new InvalidOperationException(T("Service.AttachmentsChanged"));
        }

        ValidateAttachmentLimit(keptIds.Count, newAttachmentPaths.Count);
        ValidateAttachmentSources(newAttachmentPaths);

        var removedAttachments = existing.Attachments
            .Where(attachment => !keptIds.Contains(attachment.Id))
            .ToList();
        var newAttachments = CopyAttachments(existing.Id, newAttachmentPaths);

        try
        {
            _repository.DeleteAttachments(removedAttachments.Select(attachment => attachment.Id));
            _repository.InsertAttachments(newAttachments);
        }
        catch
        {
            DeleteAttachmentFiles(newAttachments);
            throw;
        }

        DeleteAttachmentFiles(removedAttachments);
        CleanupAttachmentDirectory(existing.Id);
    }

    private void AddAttachments(string todoId, IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0)
        {
            return;
        }

        var attachments = CopyAttachments(todoId, sourcePaths);
        try
        {
            _repository.InsertAttachments(attachments);
        }
        catch
        {
            DeleteAttachmentFiles(attachments);
            throw;
        }
    }

    private List<TodoAttachment> CopyAttachments(string todoId, IReadOnlyList<string> sourcePaths)
    {
        var result = new List<TodoAttachment>();
        if (sourcePaths.Count == 0)
        {
            return result;
        }

        var taskDirectory = GetAttachmentTaskDirectory(todoId);
        Directory.CreateDirectory(taskDirectory);

        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                var originalFileName = Path.GetFileName(sourcePath);
                var storedFileName = CreateStoredFileName(originalFileName);
                var destinationPath = Path.Combine(taskDirectory, storedFileName);
                File.Copy(sourcePath, destinationPath, overwrite: false);

                var fileInfo = new FileInfo(destinationPath);
                result.Add(new TodoAttachment
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TodoId = todoId,
                    OriginalFileName = originalFileName,
                    StoredFileName = storedFileName,
                    RelativePath = CreateRelativeAttachmentPath(todoId, storedFileName),
                    FullPath = destinationPath,
                    FileSize = fileInfo.Length,
                    CreatedAt = DateTime.Now
                });
            }
        }
        catch
        {
            DeleteAttachmentFiles(result);
            CleanupAttachmentDirectory(todoId);
            throw;
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizeAttachmentPaths(IEnumerable<string>? filePaths)
    {
        if (filePaths is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(filePath);
            if (result.Any(path => string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(fullPath);
        }

        return result;
    }

    private static void ValidateAttachmentLimit(int existingCount, int newCount)
    {
        if (existingCount + newCount > MaxAttachmentCount)
        {
            throw new InvalidOperationException(F("Editor.MaxAttachmentFormat", MaxAttachmentCount));
        }
    }

    private static void ValidateAttachmentSources(IEnumerable<string> sourcePaths)
    {
        foreach (var sourcePath in sourcePaths)
        {
            if (!File.Exists(sourcePath))
            {
                throw new InvalidOperationException(F("Editor.FileMissingFormat", Path.GetFileName(sourcePath)));
            }
        }
    }

    private void DeleteAttachmentFiles(TodoItem item)
    {
        DeleteAttachmentFiles(item.Attachments);
        CleanupAttachmentDirectory(item.Id);
    }

    private static void DeleteAttachmentFiles(IEnumerable<TodoAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FullPath) || !File.Exists(attachment.FullPath))
            {
                continue;
            }

            try
            {
                File.Delete(attachment.FullPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void CleanupAttachmentDirectory(string todoId)
    {
        var taskDirectory = GetAttachmentTaskDirectory(todoId);
        if (!Directory.Exists(taskDirectory) || Directory.EnumerateFileSystemEntries(taskDirectory).Any())
        {
            return;
        }

        try
        {
            Directory.Delete(taskDirectory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string GetAttachmentTaskDirectory(string todoId)
    {
        return Path.Combine(_repository.AttachmentRootDirectory, todoId);
    }

    private static string CreateRelativeAttachmentPath(string todoId, string storedFileName)
    {
        return $"attachments/{todoId}/{storedFileName}";
    }

    private static string CreateStoredFileName(string originalFileName)
    {
        var safeFileName = SanitizeFileName(originalFileName);
        if (safeFileName.Length > 120)
        {
            var extension = Path.GetExtension(safeFileName);
            var name = Path.GetFileNameWithoutExtension(safeFileName);
            var maxNameLength = Math.Max(1, 120 - extension.Length);
            safeFileName = $"{name[..Math.Min(name.Length, maxNameLength)]}{extension}";
        }

        return $"{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid().ToString("N")[..8]}_{safeFileName}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized;
    }

    private static string? NormalizeGroupDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > 100)
        {
            throw new InvalidOperationException(T("Service.GroupDescriptionMax"));
        }

        return trimmedDescription;
    }

    private static string NormalizeIconKey(string? iconKey)
    {
        return TaskGroupIconCatalog.Options.Any(option => option.Key == iconKey) ? iconKey! : "folder";
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static string T(string key) => LocalizationService.Text(key);

    private static string F(string key, params object?[] args) => LocalizationService.Format(key, args);
}
