namespace TodoDesktopApp.Models;

public sealed class TaskExchangeDocument
{
    public int Version { get; set; } = 1;

    public string SourceApp { get; set; } = "JMtodo";

    public string? ExportedAt { get; set; }

    public List<TaskExchangeTask> Tasks { get; set; } = new();
}
