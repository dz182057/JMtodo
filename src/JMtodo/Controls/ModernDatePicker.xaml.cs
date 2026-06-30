using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace TodoDesktopApp.Controls;

public partial class ModernDatePicker : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate),
            typeof(DateTime?),
            typeof(ModernDatePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedDateChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(ModernDatePicker),
            new PropertyMetadata("选择日期"));

    public static readonly DependencyProperty DisplayFormatProperty =
        DependencyProperty.Register(
            nameof(DisplayFormat),
            typeof(string),
            typeof(ModernDatePicker),
            new PropertyMetadata("yyyy-MM-dd", OnDisplayFormatChanged));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(
            nameof(DisplayText),
            typeof(string),
            typeof(ModernDatePicker),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(
            nameof(HeaderText),
            typeof(string),
            typeof(ModernDatePicker),
            new PropertyMetadata(string.Empty));

    private DateTime _displayMonth;
    private bool _isPopupOpen;

    public ModernDatePicker()
    {
        InitializeComponent();
        _displayMonth = FirstDayOfMonth(DateTime.Today);
        RebuildCalendar();
        UpdateDisplayText();
        UpdateVisualState();
    }

    public ObservableCollection<CalendarDayItem> Days { get; } = new();

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
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

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        private set => SetValue(HeaderTextProperty, value);
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ModernDatePicker)d;
        if (picker.SelectedDate.HasValue)
        {
            picker._displayMonth = FirstDayOfMonth(picker.SelectedDate.Value);
        }

        picker.UpdateDisplayText();
        picker.RebuildCalendar();
    }

    private static void OnDisplayFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ModernDatePicker)d).UpdateDisplayText();
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

    private void PreviousMonthButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        RebuildCalendar();
    }

    private void NextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        RebuildCalendar();
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: CalendarDayItem day })
        {
            SelectedDate = day.Date;
            SetPopupOpen(false);
        }
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

    private void UpdateDisplayText()
    {
        DisplayText = SelectedDate.HasValue ? SelectedDate.Value.ToString(DisplayFormat) : string.Empty;
        DisplayTextBlock.Visibility = SelectedDate.HasValue ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderTextBlock.Visibility = SelectedDate.HasValue ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RebuildCalendar()
    {
        HeaderText = $"{_displayMonth:yyyy年M月}";
        Days.Clear();

        var firstDay = FirstDayOfMonth(_displayMonth);
        var offset = ((int)firstDay.DayOfWeek + 6) % 7;
        var start = firstDay.AddDays(-offset);
        var selected = SelectedDate?.Date;
        var today = DateTime.Today;

        for (var index = 0; index < 42; index++)
        {
            var date = start.AddDays(index);
            Days.Add(new CalendarDayItem
            {
                Date = date,
                Text = date.Day.ToString(),
                IsCurrentMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year,
                IsToday = date.Date == today,
                IsSelected = selected.HasValue && date.Date == selected.Value
            });
        }
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

    private static DateTime FirstDayOfMonth(DateTime value) => new(value.Year, value.Month, 1);
}

public sealed class CalendarDayItem
{
    public DateTime Date { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsCurrentMonth { get; set; }

    public bool IsToday { get; set; }

    public bool IsSelected { get; set; }
}
