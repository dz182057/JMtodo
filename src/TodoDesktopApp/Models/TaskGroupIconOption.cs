using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace TodoDesktopApp.Models;

public sealed class TaskGroupIconOption
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public Geometry Geometry { get; init; } = Geometry.Empty;

    public Brush Foreground { get; init; } = Brushes.Gray;

    public Brush Background { get; init; } = Brushes.Transparent;
}

public static class TaskGroupIconCatalog
{
    private static readonly SolidColorBrush Blue = CreateBrush("#3F7BFF");
    private static readonly SolidColorBrush Green = CreateBrush("#20B56B");
    private static readonly SolidColorBrush Purple = CreateBrush("#7C5CFF");
    private static readonly SolidColorBrush Amber = CreateBrush("#F59E0B");
    private static readonly SolidColorBrush Red = CreateBrush("#EF4444");
    private static readonly SolidColorBrush Slate = CreateBrush("#64748B");
    private static readonly SolidColorBrush Cyan = CreateBrush("#06B6D4");
    private static readonly SolidColorBrush BlueSoft = CreateBrush("#EEF5FF");
    private static readonly SolidColorBrush GreenSoft = CreateBrush("#EAF8F0");
    private static readonly SolidColorBrush PurpleSoft = CreateBrush("#F2EEFF");
    private static readonly SolidColorBrush AmberSoft = CreateBrush("#FFF7ED");
    private static readonly SolidColorBrush RedSoft = CreateBrush("#FFF1F2");
    private static readonly SolidColorBrush SlateSoft = CreateBrush("#F1F5F9");
    private static readonly SolidColorBrush CyanSoft = CreateBrush("#ECFEFF");

    public static IReadOnlyList<TaskGroupIconOption> Options { get; } =
    [
        Create("folder", "文件夹", Blue, BlueSoft, "M2,6 L6,6 L7.5,4 L14,4 L16,6 L16,14 L2,14 Z"),
        Create("briefcase", "工作", Green, GreenSoft, "M3,6 L15,6 L15,14 L3,14 Z M6,6 L6,4 L12,4 L12,6 M3,9 L15,9"),
        Create("book", "学习", Purple, PurpleSoft, "M4,3 L13,3 L13,15 L4,15 C3,15 2,14.2 2,13.2 L2,4.8 C2,3.8 3,3 4,3 Z M4,3 L4,15"),
        Create("home", "生活", Amber, AmberSoft, "M2.5,8 L9,3 L15.5,8 M4,7.5 L4,15 L14,15 L14,7.5"),
        Create("star", "重要", Amber, AmberSoft, "M9,2.5 L11,6.6 L15.5,7.2 L12.2,10.4 L13,15 L9,12.8 L5,15 L5.8,10.4 L2.5,7.2 L7,6.6 Z"),
        Create("flag", "目标", Red, RedSoft, "M4,15 L4,3 M4,4 L13,4 L11.5,8 L13,12 L4,12"),
        Create("clock", "临时", Slate, SlateSoft, "M9,3 A6,6 0 1 0 9,15 A6,6 0 1 0 9,3 M9,6.5 L9,9.5 L11.5,11"),
        Create("tag", "标签", Cyan, CyanSoft, "M3,4 L10,4 L15,9 L9,15 L3,9 Z M7,7 L7.1,7"),
        Create("archive", "归档", Purple, PurpleSoft, "M3,5 L15,5 L15,8 L3,8 Z M4,8 L4,15 L14,15 L14,8 M7,11 L11,11"),
        Create("lightbulb", "灵感", Amber, AmberSoft, "M9,3 A4,4 0 0 0 6.5,10.2 L6.5,12 L11.5,12 L11.5,10.2 A4,4 0 0 0 9,3 M7,15 L11,15"),
        Create("all", "全部任务", Blue, BlueSoft, "M4,4 L14,4 M4,8 L14,8 M4,12 L14,12 M2.5,4 L2.6,4 M2.5,8 L2.6,8 M2.5,12 L2.6,12"),
        Create("inbox", "未分组", Slate, SlateSoft, "M3,6 L15,6 L15,13 L12,13 L11,15 L7,15 L6,13 L3,13 Z M5,6 L6.5,3 L11.5,3 L13,6")
    ];

    public static TaskGroupIconOption Get(string? key)
    {
        return Options.FirstOrDefault(option => option.Key == key) ?? Options[0];
    }

    private static TaskGroupIconOption Create(string key, string name, Brush foreground, Brush background, string geometry)
    {
        return new TaskGroupIconOption
        {
            Key = key,
            Name = name,
            Foreground = foreground,
            Background = background,
            Geometry = Geometry.Parse(geometry)
        };
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
