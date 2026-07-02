namespace TodoDesktopApp.Models;

public sealed class TodoGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IconKey { get; set; } = "folder";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int SortOrder { get; set; }
}
