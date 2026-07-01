namespace TodoDesktopApp.Models;

public sealed class TaskExchangeTask
{
    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string? StartDate { get; set; }

    public string? DueDate { get; set; }

    public string? Status { get; set; }

    public string? GroupName { get; set; }

    public List<TaskExchangeAttachment> Attachments { get; set; } = new();

    public List<TaskExchangeTask> Subtasks { get; set; } = new();
}
