using System.Windows;

namespace TodoDesktopApp.Services;

public sealed class WindowLevelService
{
    public void BringToFrontTemporarily(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Topmost = true;
        window.Activate();
    }
}
