using Microsoft.Data.Sqlite;
using System.IO;
using TodoDesktopApp.Models;

namespace TodoDesktopApp.Data;

public sealed class TodoRepository
{
    private const int SchemaVersion = 4;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public TodoRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDirectory = Path.Combine(appData, "TodoDesktopApp");
        Directory.CreateDirectory(dataDirectory);

        _databasePath = Path.Combine(dataDirectory, "todos.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString();
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        CreateTables(connection);
        EnsureGroupColumns(connection);
        var addedSortOrder = EnsureTodoSortOrderColumn(connection);
        if (addedSortOrder)
        {
            NormalizeTodoSortOrders(connection);
        }

        SetSchemaVersion(connection);
    }

    public IReadOnlyList<TodoGroup> GetGroups()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, icon_key, description, created_at
            FROM todo_groups
            ORDER BY name COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var groups = new List<TodoGroup>();
        while (reader.Read())
        {
            groups.Add(new TodoGroup
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                IconKey = reader.IsDBNull(2) ? "folder" : reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4))
            });
        }

        return groups;
    }

    public void InsertGroup(TodoGroup group)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO todo_groups (id, name, icon_key, description, created_at)
            VALUES ($id, $name, $icon_key, $description, $created_at);
            """;
        command.Parameters.AddWithValue("$id", group.Id);
        command.Parameters.AddWithValue("$name", group.Name);
        command.Parameters.AddWithValue("$icon_key", NormalizeIconKey(group.IconKey));
        command.Parameters.AddWithValue("$description", (object?)group.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatDateTime(group.CreatedAt));
        command.ExecuteNonQuery();
    }

    public void UpdateGroup(TodoGroup group)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE todo_groups
            SET name = $name,
                icon_key = $icon_key,
                description = $description
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", group.Id);
        command.Parameters.AddWithValue("$name", group.Name);
        command.Parameters.AddWithValue("$icon_key", NormalizeIconKey(group.IconKey));
        command.Parameters.AddWithValue("$description", (object?)group.Description ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void DeleteGroup(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var clearCommand = connection.CreateCommand())
        {
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = "UPDATE todos SET group_id = NULL WHERE group_id = $id;";
            clearCommand.Parameters.AddWithValue("$id", id);
            clearCommand.ExecuteNonQuery();
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM todo_groups WHERE id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", id);
            deleteCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void CreateTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS todo_groups (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                icon_key TEXT NOT NULL DEFAULT 'folder',
                description TEXT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS todos (
                id TEXT PRIMARY KEY,
                parent_id TEXT NULL,
                group_id TEXT NULL,
                title TEXT NOT NULL,
                note TEXT NULL,
                start_date TEXT NOT NULL,
                due_date TEXT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                completed_at TEXT NULL,
                deleted_at TEXT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureGroupColumns(SqliteConnection connection)
    {
        if (!ColumnExists(connection, "todo_groups", "icon_key"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE todo_groups ADD COLUMN icon_key TEXT NOT NULL DEFAULT 'folder';";
            command.ExecuteNonQuery();
        }

        if (!ColumnExists(connection, "todo_groups", "description"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE todo_groups ADD COLUMN description TEXT NULL;";
            command.ExecuteNonQuery();
        }
    }

    private static bool EnsureTodoSortOrderColumn(SqliteConnection connection)
    {
        if (ColumnExists(connection, "todos", "sort_order"))
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE todos ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;";
        command.ExecuteNonQuery();
        return true;
    }

    private static void NormalizeTodoSortOrders(SqliteConnection connection)
    {
        var rows = new List<(string Id, string? ParentId, DateOnly? DueDate, DateOnly StartDate, DateTime CreatedAt)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, parent_id, due_date, start_date, created_at
                FROM todos
                WHERE status = 'Active';
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : DateOnly.Parse(reader.GetString(2)),
                    DateOnly.Parse(reader.GetString(3)),
                    DateTime.Parse(reader.GetString(4))));
            }
        }

        using var transaction = connection.BeginTransaction();
        foreach (var siblingGroup in rows.GroupBy(row => row.ParentId ?? string.Empty))
        {
            var sortOrder = 10;
            foreach (var row in siblingGroup
                         .OrderBy(row => row.DueDate.HasValue ? 0 : 1)
                         .ThenBy(row => row.DueDate)
                         .ThenBy(row => row.StartDate)
                         .ThenBy(row => row.CreatedAt))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE todos SET sort_order = $sort_order WHERE id = $id;";
                command.Parameters.AddWithValue("$id", row.Id);
                command.Parameters.AddWithValue("$sort_order", sortOrder);
                command.ExecuteNonQuery();
                sortOrder += 10;
            }
        }

        transaction.Commit();
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TodoItem> Search(TodoSearchCriteria criteria)
    {
        var items = GetAll();
        var query = items.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(criteria.Keyword))
        {
            var keyword = criteria.Keyword.Trim();
            query = query.Where(todo =>
                todo.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (todo.Note?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (criteria.Status.HasValue)
        {
            query = query.Where(todo => todo.Status == criteria.Status.Value);
        }

        if (criteria.StartDateFrom.HasValue)
        {
            query = query.Where(todo => todo.StartDate >= criteria.StartDateFrom.Value);
        }

        if (criteria.StartDateTo.HasValue)
        {
            query = query.Where(todo => todo.StartDate <= criteria.StartDateTo.Value);
        }

        var hasDueDateFilter = criteria.DueDateFrom.HasValue || criteria.DueDateTo.HasValue;
        if (!criteria.IncludeNoDue && !hasDueDateFilter)
        {
            query = query.Where(todo => todo.DueDate.HasValue);
        }

        if (hasDueDateFilter)
        {
            query = query.Where(todo =>
            {
                if (!todo.DueDate.HasValue)
                {
                    return criteria.IncludeNoDue;
                }

                if (criteria.DueDateFrom.HasValue && todo.DueDate.Value < criteria.DueDateFrom.Value)
                {
                    return false;
                }

                if (criteria.DueDateTo.HasValue && todo.DueDate.Value > criteria.DueDateTo.Value)
                {
                    return false;
                }

                return true;
            });
        }

        if (criteria.CreatedAtFrom.HasValue)
        {
            query = query.Where(todo => todo.CreatedAt >= criteria.CreatedAtFrom.Value.Date);
        }

        if (criteria.CreatedAtTo.HasValue)
        {
            var end = criteria.CreatedAtTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(todo => todo.CreatedAt <= end);
        }

        if (criteria.UpdatedAtFrom.HasValue)
        {
            query = query.Where(todo => todo.UpdatedAt >= criteria.UpdatedAtFrom.Value.Date);
        }

        if (criteria.UpdatedAtTo.HasValue)
        {
            var end = criteria.UpdatedAtTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(todo => todo.UpdatedAt <= end);
        }

        return SortSearchResults(query.ToList(), items);
    }

    public IReadOnlyList<TodoItem> GetCurrentActive(DateOnly today)
    {
        return Search(new TodoSearchCriteria { Status = TodoStatus.Active })
            .Where(todo => todo.StartDate <= today)
            .OrderBy(todo => todo.DueDate.HasValue ? 0 : 1)
            .ThenBy(todo => todo.DueDate)
            .ThenBy(todo => todo.StartDate)
            .ThenBy(todo => todo.CreatedAt)
            .ToList();
    }

    public TodoItem? GetById(string id)
    {
        return GetAll().FirstOrDefault(todo => todo.Id == id);
    }

    public void Insert(TodoItem item)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO todos (
                id, parent_id, group_id, title, note, start_date, due_date, status,
                created_at, updated_at, completed_at, deleted_at, sort_order
            )
            VALUES (
                $id, $parent_id, $group_id, $title, $note, $start_date, $due_date, $status,
                $created_at, $updated_at, $completed_at, $deleted_at, $sort_order
            );
            """;
        AddParameters(command, item);
        command.ExecuteNonQuery();
    }

    public void Update(TodoItem item)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE todos
            SET parent_id = $parent_id,
                group_id = $group_id,
                title = $title,
                note = $note,
                start_date = $start_date,
                due_date = $due_date,
                status = $status,
                created_at = $created_at,
                updated_at = $updated_at,
                completed_at = $completed_at,
                deleted_at = $deleted_at,
                sort_order = $sort_order
            WHERE id = $id;
            """;
        AddParameters(command, item);
        command.ExecuteNonQuery();
    }

    public void UpdateSortOrders(IReadOnlyList<(string Id, int SortOrder)> items)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var item in items)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE todos SET sort_order = $sort_order WHERE id = $id;";
            command.Parameters.AddWithValue("$id", item.Id);
            command.Parameters.AddWithValue("$sort_order", item.SortOrder);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public int GetNextActiveSortOrder(string? parentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = parentId is null
            ? "SELECT COALESCE(MAX(sort_order), 0) + 10 FROM todos WHERE status = 'Active' AND parent_id IS NULL;"
            : "SELECT COALESCE(MAX(sort_order), 0) + 10 FROM todos WHERE status = 'Active' AND parent_id = $parent_id;";
        if (parentId is not null)
        {
            command.Parameters.AddWithValue("$parent_id", parentId);
        }

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void DeletePermanent(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM todos WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<TodoItem> GetAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT todos.id,
                   todos.parent_id,
                   todos.group_id,
                   todos.title,
                   todos.note,
                   todos.start_date,
                   todos.due_date,
                   todos.status,
                   todos.created_at,
                   todos.updated_at,
                   todos.completed_at,
                   todos.deleted_at,
                   todos.sort_order,
                   parent.title AS parent_title,
                   todo_groups.name AS group_name
            FROM todos
            LEFT JOIN todos AS parent ON parent.id = todos.parent_id
            LEFT JOIN todo_groups ON todo_groups.id = todos.group_id;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<TodoItem>();
        while (reader.Read())
        {
            items.Add(ReadTodo(reader));
        }

        return items;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void AddParameters(SqliteCommand command, TodoItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$parent_id", (object?)item.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$group_id", (object?)item.GroupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$note", (object?)item.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$start_date", FormatDate(item.StartDate));
        command.Parameters.AddWithValue("$due_date", item.DueDate.HasValue ? FormatDate(item.DueDate.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$created_at", FormatDateTime(item.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", FormatDateTime(item.UpdatedAt));
        command.Parameters.AddWithValue("$completed_at", item.CompletedAt.HasValue ? FormatDateTime(item.CompletedAt.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$deleted_at", item.DeletedAt.HasValue ? FormatDateTime(item.DeletedAt.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$sort_order", item.SortOrder);
    }

    private static TodoItem ReadTodo(SqliteDataReader reader)
    {
        return new TodoItem
        {
            Id = reader.GetString(0),
            ParentId = reader.IsDBNull(1) ? null : reader.GetString(1),
            GroupId = reader.IsDBNull(2) ? null : reader.GetString(2),
            Title = reader.GetString(3),
            Note = reader.IsDBNull(4) ? null : reader.GetString(4),
            StartDate = DateOnly.Parse(reader.GetString(5)),
            DueDate = reader.IsDBNull(6) ? null : DateOnly.Parse(reader.GetString(6)),
            Status = Enum.Parse<TodoStatus>(reader.GetString(7)),
            CreatedAt = DateTime.Parse(reader.GetString(8)),
            UpdatedAt = DateTime.Parse(reader.GetString(9)),
            CompletedAt = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
            DeletedAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
            SortOrder = reader.GetInt32(12),
            ParentTitle = reader.IsDBNull(13) ? null : reader.GetString(13),
            GroupName = reader.IsDBNull(14) ? null : reader.GetString(14)
        };
    }

    private static IReadOnlyList<TodoItem> SortSearchResults(IReadOnlyList<TodoItem> filteredItems, IReadOnlyList<TodoItem> allItems)
    {
        var rootSortOrders = allItems
            .Where(todo => !todo.IsSubtask)
            .GroupBy(todo => todo.Id)
            .ToDictionary(group => group.Key, group => group.First().SortOrder);

        return filteredItems
            .OrderBy(StatusRank)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? GetRootSortOrder(todo, rootSortOrders) : int.MaxValue)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? (todo.IsSubtask ? 1 : 0) : 0)
            .ThenBy(todo => todo.Status == TodoStatus.Active && todo.IsSubtask ? todo.SortOrder : 0)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? todo.CreatedAt : DateTime.MinValue)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? string.Empty : string.IsNullOrWhiteSpace(todo.GroupName) ? "1" : "0")
            .ThenBy(todo => todo.Status == TodoStatus.Active ? string.Empty : todo.GroupDisplayText)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? string.Empty : todo.ParentId ?? todo.Id)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? 0 : todo.IsSubtask ? 1 : 0)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? 0 : todo.DueDate.HasValue ? 0 : 1)
            .ThenBy(todo => todo.Status == TodoStatus.Active ? null : todo.DueDate)
            .ThenByDescending(todo => todo.Status == TodoStatus.Active ? DateOnly.MinValue : todo.StartDate)
            .ThenByDescending(todo => todo.Status == TodoStatus.Active ? DateTime.MinValue : todo.UpdatedAt)
            .ToList();
    }

    private static int GetRootSortOrder(TodoItem todo, IReadOnlyDictionary<string, int> rootSortOrders)
    {
        if (!todo.IsSubtask)
        {
            return todo.SortOrder;
        }

        return todo.ParentId is not null && rootSortOrders.TryGetValue(todo.ParentId, out var sortOrder)
            ? sortOrder
            : int.MaxValue;
    }

    private static int StatusRank(TodoItem todo)
    {
        return todo.Status == TodoStatus.Active ? 0 : todo.Status == TodoStatus.Completed ? 1 : 2;
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static string FormatDateTime(DateTime dateTime) => dateTime.ToString("O");

    private static string NormalizeIconKey(string? iconKey)
    {
        return string.IsNullOrWhiteSpace(iconKey) ? "folder" : iconKey;
    }
}
