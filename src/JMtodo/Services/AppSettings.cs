namespace TodoDesktopApp.Services;

public sealed class AppSettings
{
    public double FloatingWindowX { get; set; } = 80;
    public double FloatingWindowY { get; set; } = 120;
    public double FloatingWindowWidth { get; set; } = 260;
    public double FloatingWindowHeight { get; set; } = 330;
    public string LastOpenedManagementWindowState { get; set; } = "Normal";
    public string Language { get; set; } = LocalizationService.DefaultLanguage;
}
