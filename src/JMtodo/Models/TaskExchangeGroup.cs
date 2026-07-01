namespace TodoDesktopApp.Models;

public sealed class TaskExchangeGroup
{
    public string Name { get; set; } = string.Empty;

    public string IconKey { get; set; } = "folder";

    public string? Description { get; set; }
}
