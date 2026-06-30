using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace TodoDesktopApp.Controls;

public partial class ModernDateRangePicker : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StartDateProperty =
        DependencyProperty.Register(
            nameof(StartDate),
            typeof(DateTime?),
            typeof(ModernDateRangePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnDateChanged));

    public static readonly DependencyProperty EndDateProperty =
        DependencyProperty.Register(
            nameof(EndDate),
            typeof(DateTime?),
            typeof(ModernDateRangePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnDateChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(ModernDateRangePicker),
            new PropertyMetadata("-"));

    public static readonly DependencyProperty StartLabelProperty =
        DependencyProperty.Register(
            nameof(StartLabel),
            typeof(string),
            typeof(ModernDateRangePicker),
            new PropertyMetadata("开始"));

    public static readonly DependencyProperty EndLabelProperty =
        DependencyProperty.Register(
            nameof(EndLabel),
            typeof(string),
            typeof(ModernDateRangePicker),
            new PropertyMetadata("结束"));

    public static readonly DependencyProperty DisplayFormatProperty =
        DependencyProperty.Register(
            nameof(DisplayFormat),
            typeof(string),
            typeof(ModernDateRangePicker),
            new PropertyMetadata("yyyy-MM-dd", OnDisplayFormatChanged));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(
            nameof(DisplayText),
            typeof(string),
            typeof(ModernDateRangePicker),
            new PropertyMetadata(string.Empty));

    private bool _isPopupOpen;

    public ModernDateRangePicker()
    {
        InitializeComponent();
        UpdateDisplayText();
        UpdateVisualState();
    }

    public DateTime? StartDate
    {
        get => (DateTime?)GetValue(StartDateProperty);
        set => SetValue(StartDateProperty, value);
    }

    public DateTime? EndDate
    {
        get => (DateTime?)GetValue(EndDateProperty);
        set => SetValue(EndDateProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string StartLabel
    {
        get => (string)GetValue(StartLabelProperty);
        set => SetValue(StartLabelProperty, value);
    }

    public string EndLabel
    {
        get => (string)GetValue(EndLabelProperty);
        set => SetValue(EndLabelProperty, value);
    }

    public string DisplayFormat
    {
        get => (string)GetValue(DisplayFormatProperty);
        set => SetValue(DisplayFormatProperty, value);
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    private static void OnDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ModernDateRangePicker)d).UpdateDisplayText();
    }

    private static void OnDisplayFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ModernDateRangePicker)d).UpdateDisplayText();
    }

    private void InputBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        var shouldOpen = !CalendarPopup.IsOpen;
        Focus();
        e.Handled = true;

        Dispatcher.BeginInvoke(
            () =>
            {
                if (IsEnabled)
                {
                    SetPopupOpen(shouldOpen);
                }
            },
            DispatcherPriority.ApplicationIdle);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        StartDate = null;
        EndDate = null;
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        SetPopupOpen(false);
    }

    private void Calendar_SelectedDatesChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(ReleaseCalendarMouseCapture, DispatcherPriority.Input);
    }

    private void CalendarPopup_Closed(object? sender, EventArgs e)
    {
        _isPopupOpen = false;
        UpdateVisualState();
    }

    private void RootControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CalendarPopup.IsOpen)
        {
            SetPopupOpen(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && CalendarPopup.IsOpen)
        {
            SetPopupOpen(false);
            e.Handled = true;
        }
    }

    private void RootControl_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsEnabled)
        {
            SetPopupOpen(false);
        }

        UpdateVisualState();
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateVisualState();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateVisualState();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        UpdateVisualState();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (!CalendarPopup.IsOpen)
        {
            UpdateVisualState();
        }
    }

    private void SetPopupOpen(bool isOpen)
    {
        _isPopupOpen = isOpen;
        CalendarPopup.IsOpen = isOpen;
        UpdateVisualState();
    }

    private void ReleaseCalendarMouseCapture()
    {
        if (Mouse.Captured is DependencyObject captured &&
            (IsDescendantOf(captured, StartCalendar) || IsDescendantOf(captured, EndCalendar)))
        {
            Mouse.Capture(null);
        }
    }

    private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateDisplayText()
    {
        var hasStart = StartDate.HasValue;
        var hasEnd = EndDate.HasValue;

        if (!hasStart && !hasEnd)
        {
            DisplayText = string.Empty;
            DisplayTextBlock.Visibility = Visibility.Collapsed;
            PlaceholderTextBlock.Visibility = Visibility.Visible;
            return;
        }

        var startText = hasStart ? StartDate!.Value.ToString(DisplayFormat) : string.Empty;
        var endText = hasEnd ? EndDate!.Value.ToString(DisplayFormat) : string.Empty;
        DisplayText = $"{startText} - {endText}";
        DisplayTextBlock.Visibility = Visibility.Visible;
        PlaceholderTextBlock.Visibility = Visibility.Collapsed;
    }

    private void UpdateVisualState()
    {
        if (!IsEnabled)
        {
            InputBorder.Background = new SolidColorBrush(MediaColor.FromRgb(0xF2, 0xF5, 0xFA));
            InputBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xE3, 0xE9, 0xF2));
            DisplayTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xA6, 0xB0, 0xC0));
            PlaceholderTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xA6, 0xB0, 0xC0));
            Opacity = 1;
            return;
        }

        InputBorder.Background = MediaBrushes.White;
        DisplayTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x1F, 0x29, 0x37));
        PlaceholderTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xA6, 0xB0, 0xC0));

        if (_isPopupOpen || IsKeyboardFocusWithin)
        {
            InputBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x3F, 0x7B, 0xFF));
        }
        else if (IsMouseOver)
        {
            InputBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xC9, 0xD6, 0xE8));
        }
        else
        {
            InputBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xDD, 0xE5, 0xF2));
        }
    }
}
