using TodoDesktopApp.Data;
using TodoDesktopApp.Models;

namespace TodoDesktopApp.Services;

public sealed class TodoService
{
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
        var todos = Search(new TodoSearchCriteria { IncludeNoDue = true })
            .Where(todo => todo.Status == TodoStatus.Active && todo.StartDate <= today)
            .ToList();

        var result = new List<TodoItem>();
        var roots = todos
            .Where(todo => !todo.IsSubtask)
            .OrderBy(todo => todo.SortOrder)
            .ThenBy(todo => todo.CreatedAt)
            .ToList();

        foreach (var root in roots)
        {
            result.Add(root);

            result.AddRange(todos
                .Where(todo => todo.ParentId == root.Id)
                .OrderBy(todo => todo.SortOrder)
                .ThenBy(todo => todo.CreatedAt));
        }

        result.AddRange(todos
            .Where(todo => todo.IsSubtask && roots.All(root => root.Id != todo.ParentId))
            .OrderBy(todo => todo.SortOrder)
            .ThenBy(todo => todo.CreatedAt));

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
            throw new InvalidOperationException("已存在同名任务组。");
        }

        var group = new TodoGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmedName,
            IconKey = NormalizeIconKey(iconKey),
            Description = trimmedDescription,
            CreatedAt = DateTime.Now
        };

        _repository.InsertGroup(group);
        NotifyChanged();
        return group;
    }

    public void RenameGroup(string id, string name)
    {
        var group = _repository.GetGroups().FirstOrDefault(group => group.Id == id)
            ?? throw new InvalidOperationException("找不到要修改的任务组。");
        UpdateGroup(id, name, group.IconKey, group.Description);
    }

    public void UpdateGroup(string id, string name, string iconKey, string? description)
    {
        var trimmedName = ValidateGroupName(name);
        var trimmedDescription = NormalizeGroupDescription(description);
        var groups = _repository.GetGroups();
        var group = groups.FirstOrDefault(group => group.Id == id) ?? throw new InvalidOperationException("找不到要修改的任务组。");
        if (groups.Any(item => item.Id != id && string.Equals(item.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("已存在同名任务组。");
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
            throw new InvalidOperationException("找不到要删除的任务组。");
        }

        _repository.DeleteGroup(id);
        NotifyChanged();
    }

    public TodoItem Create(string title, string? note, DateOnly startDate, DateOnly? dueDate, string? groupId = null)
    {
        return CreateTodo(title, note, startDate, dueDate, parentId: null, groupId);
    }

    public TodoItem CreateSubtask(string parentId, string title, string? note, DateOnly startDate, DateOnly? dueDate)
    {
        var parent = _repository.GetById(parentId) ?? throw new InvalidOperationException("找不到父任务。");
        if (parent.IsSubtask)
        {
            throw new InvalidOperationException("暂时只支持二级子任务。");
        }

        if (parent.Status == TodoStatus.Deleted)
        {
            throw new InvalidOperationException("已删除任务不能添加子任务。");
        }

        if (parent.Status != TodoStatus.Active)
        {
            throw new InvalidOperationException("只有未完成的一级任务可以添加子任务。");
        }

        return CreateTodo(title, note, startDate, dueDate, parent.Id, parent.GroupId);
    }

    private TodoItem CreateTodo(string title, string? note, DateOnly startDate, DateOnly? dueDate, string? parentId, string? groupId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("任务标题不能为空。");
        }

        if (!string.IsNullOrWhiteSpace(groupId) && _repository.GetGroups().All(group => group.Id != groupId))
        {
            throw new InvalidOperationException("找不到选择的任务组。");
        }

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
        NotifyChanged();
        return item;
    }

    public void Update(TodoItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            throw new InvalidOperationException("任务标题不能为空。");
        }

        var existing = _repository.GetById(item.Id) ?? throw new InvalidOperationException("找不到要更新的任务。");
        existing.Title = item.Title.Trim();
        existing.Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim();
        existing.StartDate = item.StartDate;
        existing.DueDate = item.DueDate;
        existing.UpdatedAt = DateTime.Now;

        _repository.Update(existing);
        NotifyChanged();
    }

    public void Complete(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException("找不到要完成的任务。");
        item.Status = TodoStatus.Completed;
        item.CompletedAt = DateTime.Now;
        item.DeletedAt = null;
        item.UpdatedAt = DateTime.Now;
        _repository.Update(item);
        NotifyChanged();
    }

    public void Reopen(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException("找不到要恢复为未完成的任务。");
        item.Status = TodoStatus.Active;
        item.CompletedAt = null;
        item.DeletedAt = null;
        item.UpdatedAt = DateTime.Now;
        _repository.Update(item);
        NotifyChanged();
    }

    public void SoftDelete(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException("找不到要删除的任务。");
        item.Status = TodoStatus.Deleted;
        item.DeletedAt = DateTime.Now;
        item.UpdatedAt = DateTime.Now;
        _repository.Update(item);
        NotifyChanged();
    }

    public void Restore(string id)
    {
        var item = _repository.GetById(id) ?? throw new InvalidOperationException("找不到要恢复的任务。");
        item.Status = TodoStatus.Active;
        item.DeletedAt = null;
        item.CompletedAt = null;
        item.UpdatedAt = DateTime.Now;
        _repository.Update(item);
        NotifyChanged();
    }

    public void DeletePermanent(string id)
    {
        _repository.DeletePermanent(id);
        NotifyChanged();
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

        var ordered = siblings.OrderBy(todo => todo.SortOrder).ThenBy(todo => todo.CreatedAt).ToList();
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

    private static string ValidateGroupName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("任务组名称不能为空。");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 20)
        {
            throw new InvalidOperationException("任务组名称不能超过 20 字。");
        }

        return trimmedName;
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
            throw new InvalidOperationException("任务组描述不能超过 100 字。");
        }

        return trimmedDescription;
    }

    private static string NormalizeIconKey(string? iconKey)
    {
        return TaskGroupIconCatalog.Options.Any(option => option.Key == iconKey) ? iconKey! : "folder";
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
