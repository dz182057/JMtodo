using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;
using TodoDesktopApp.Views;
using Forms = System.Windows.Forms;

namespace TodoDesktopApp;

public partial class FloatingTaskWindow : Window
{
    private const double DefaultFloatingWidth = 260;
    private const double DefaultFloatingHeight = 330;
    private const double MinFloatingWidth = 240;
    private const double MinFloatingHeight = 260;
    private const double MaxFloatingWidth = 420;
    private const double MaxFloatingHeight = 560;
    private const double FloatingShadowMargin = 18;
    private const double FloatingShadowInset = FloatingShadowMargin * 2;
    private const double EdgeSnapThreshold = 24;
    private const double EdgeDetachInset = EdgeSnapThreshold + 12;
    private const double EdgeDetachDragDistance = 56;
    private const double EdgeDetachDirectionRatio = 1.2;
    private const double EdgeIconSize = 56;
    private const double EdgePeekSize = 18;
    private const double EdgePreviewGap = 10;
    private readonly FloatingViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly WindowLevelService _windowLevelService;
    private AppSettings _settings;
    private bool _allowClose;
    private bool _isInitialized;
    private bool _isEdgeDocked;
    private bool _isEdgePreviewOpen;
    private bool _isDraggingEdgeIcon;
    private bool _edgeIconDetachedDuringDrag;
    private bool _suppressSettingsSave;
    private DockEdge _dockEdge;
    private System.Windows.Point _edgeDragStartMouse;
    private System.Windows.Point? _taskDragStart;
    private FloatingTaskItemViewModel? _draggedTask;
    private double _expandedHeight = DefaultFloatingHeight;
    private double _expandedWidth = DefaultFloatingWidth;
    private double _expandedLeft = 80;
    private double _expandedTop = 120;
    private double _edgeIconLeft = 80;
    private double _edgeIconTop = 120;
    private double _edgeDragStartLeft;
    private double _edgeDragStartTop;
    private double _edgeDragOffsetX;
    private double _edgeDragOffsetY;
    private double _floatingDragOffsetX;
    private double _floatingDragOffsetY;

    public FloatingTaskWindow(
        FloatingViewModel viewModel,
        SettingsService settingsService,
        WindowLevelService windowLevelService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        _windowLevelService = windowLevelService;
        _settings = _settingsService.Load();
        DataContext = _viewModel;

        ApplySettings();
        _viewModel.VisibleTasksChanged += (_, _) => RefreshVisibilityFromTasks(activate: IsVisible);
        _isInitialized = true;
    }

    public void RefreshVisibilityFromTasks(bool activate)
    {
        if (_viewModel.Todos.Count == 0)
        {
            Hide();
            return;
        }

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        if (activate)
        {
            _windowLevelService.BringToFrontTemporarily(this);
        }
    }

    public void ToggleFromUserRequest()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if (_viewModel.Todos.Count == 0)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.NoCurrentTask.Title"), T("Dialog.NoCurrentTask.Message"));
            return;
        }

        RefreshVisibilityFromTasks(activate: true);
    }

    public void ShowExpandedFromUserRequest()
    {
        if (_viewModel.Todos.Count == 0)
        {
            Hide();
            return;
        }

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        if (_isEdgeDocked)
        {
            RestoreEdgeDockToExpanded();
        }

        _windowLevelService.BringToFrontTemporarily(this);
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: FloatingTaskItemViewModel todo } checkBox)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            _viewModel.Complete(todo);
        }
        else
        {
            _viewModel.Reopen(todo);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _dockEdge = DockEdge.None;
            DragMove();
            TryDockToNearestEdge();
            if (!_isEdgeDocked)
            {
                SaveSettings();
            }
        }
    }

    private void AddSubtaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not FrameworkElement { Tag: FloatingTaskItemViewModel parent })
        {
            return;
        }

        if (parent.SourceTask.IsSubtask)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.CannotAddSubtask.Title"), T("Dialog.CannotAddSubtask.DepthMessage"));
            return;
        }

        if (parent.SourceTask.Status != TodoStatus.Active)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.CannotAddSubtask.Title"), T("Dialog.CannotAddSubtask.StatusMessage"));
            return;
        }

        var editor = new TodoEditorViewModel(parent.SourceTask, isNewSubtask: true);
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = T("Main.AddSubtask") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _viewModel.CreateSubtask(
            parent,
            editor.Title,
            editor.Note,
            editor.GetStartDate(),
            editor.GetDueDate(),
            editor.GetNewAttachmentPaths());
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new TodoEditorViewModel(_viewModel.GetGroups());
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = T("Tray.AddTask") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _viewModel.Create(
            editor.Title,
            editor.Note,
            editor.GetStartDate(),
            editor.GetDueDate(),
            editor.SelectedGroupId,
            editor.GetNewAttachmentPaths());
    }

    private void EditTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FloatingTaskItemViewModel todo)
        {
            return;
        }

        var editor = new TodoEditorViewModel(todo.SourceTask.Clone());
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = T("Floating.EditTask") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _viewModel.Update(
            todo,
            editor.Title,
            editor.Note,
            editor.GetStartDate(),
            editor.GetDueDate(),
            editor.GetKeptAttachmentIds(),
            editor.GetNewAttachmentPaths());
    }

    private void FloatingAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoAttachment attachment)
        {
            return;
        }

        try
        {
            _viewModel.OpenAttachment(attachment);
        }
        catch (InvalidOperationException ex)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.OpenFileFailed.Title"), ex.Message);
        }
    }

    private void FloatingTaskCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _taskDragStart = null;
        _draggedTask = null;
        if (IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var todo = GetFloatingTask(e.OriginalSource as DependencyObject);
        if (todo?.Status == TodoStatus.Active)
        {
            _taskDragStart = e.GetPosition(this);
            _draggedTask = todo;
        }
    }

    private void FloatingTaskCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_taskDragStart is null || _draggedTask is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _taskDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _taskDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _draggedTask;
        _taskDragStart = null;
        _draggedTask = null;
        System.Windows.DragDrop.DoDragDrop(
            (DependencyObject)sender,
            new System.Windows.DataObject(typeof(FloatingTaskItemViewModel), dragged),
            System.Windows.DragDropEffects.Move);
    }

    private void FloatingTaskCard_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(FloatingTaskItemViewModel)) as FloatingTaskItemViewModel;
        var target = GetFloatingTask(e.OriginalSource as DependencyObject);
        e.Effects = CanReorder(dragged, target) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void FloatingTaskCard_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(FloatingTaskItemViewModel)) as FloatingTaskItemViewModel;
        var targetHost = FindFloatingTaskHost(e.OriginalSource as DependencyObject) ?? sender as FrameworkElement;
        var target = targetHost?.Tag as FloatingTaskItemViewModel;
        if (!CanReorder(dragged, target))
        {
            e.Handled = true;
            return;
        }

        var insertBefore = targetHost is null || e.GetPosition(targetHost).Y < targetHost.ActualHeight / 2;
        _viewModel.TryReorder(dragged!.TaskId, target!.TaskId, insertBefore);
        e.Handled = true;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Content = Topmost ? "●" : "○";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isEdgeDocked || _isEdgePreviewOpen)
        {
            return;
        }

        Width = Clamp(Width + e.HorizontalChange, MinWidth, MaxWidth);
        Height = Clamp(Height + e.VerticalChange, MinHeight, MaxHeight);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            SaveSettings();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void Window_PositionOrSizeChanged(object sender, EventArgs e)
    {
        if (!_isInitialized || _isEdgeDocked || _suppressSettingsSave)
        {
            return;
        }

        SaveSettings();
    }

    private void EdgeIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isEdgeDocked || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        if (_isEdgePreviewOpen)
        {
            ReturnToEdgeIcon();
        }

        _isDraggingEdgeIcon = true;
        _edgeIconDetachedDuringDrag = false;
        _edgeDragStartMouse = GetMouseScreenPoint(e.GetPosition(this));
        _edgeDragStartLeft = Left;
        _edgeDragStartTop = Top;
        _edgeDragOffsetX = _edgeDragStartMouse.X - Left;
        _edgeDragOffsetY = _edgeDragStartMouse.Y - Top;
        Root.CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingEdgeIcon)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishEdgeIconDrag();
            return;
        }

        var mouseScreenPoint = GetMouseScreenPoint(e.GetPosition(this));
        if (!_edgeIconDetachedDuringDrag && ShouldDetachEdgeIcon(mouseScreenPoint))
        {
            _edgeIconDetachedDuringDrag = true;
            RestoreEdgeIconToFloating(mouseScreenPoint);
            _floatingDragOffsetX = mouseScreenPoint.X - Left;
            _floatingDragOffsetY = mouseScreenPoint.Y - Top;
        }

        if (_edgeIconDetachedDuringDrag)
        {
            MoveFloatingWithMouse(mouseScreenPoint);
        }
        else
        {
            MoveEdgeIconWithMouse(mouseScreenPoint);
        }

        e.Handled = true;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingEdgeIcon)
        {
            return;
        }

        FinishEdgeIconDrag();
        e.Handled = true;
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isEdgePreviewOpen && !_isDraggingEdgeIcon)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (_isEdgePreviewOpen && !_isDraggingEdgeIcon && !IsCursorInsideWindow())
                    {
                        ReturnToEdgeIcon();
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void EdgeIcon_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isEdgeDocked && !_isDraggingEdgeIcon && !_isEdgePreviewOpen)
        {
            ShowEdgePreview();
        }
    }

    private void ApplySettings()
    {
        var normalizedSize = NormalizeLegacyDefaultSize();
        Left = _settings.FloatingWindowX - FloatingShadowMargin;
        Top = _settings.FloatingWindowY - FloatingShadowMargin;
        MinWidth = ToWindowWidth(MinFloatingWidth);
        MinHeight = ToWindowHeight(MinFloatingHeight);
        MaxWidth = ToWindowWidth(MaxFloatingWidth);
        MaxHeight = ToWindowHeight(MaxFloatingHeight);
        Width = ToWindowWidth(SanitizeFloatingWidth(_settings.FloatingWindowWidth));
        Height = ToWindowHeight(SanitizeFloatingHeight(_settings.FloatingWindowHeight));
        ClampExpandedSizeToWorkArea();
        _expandedWidth = SanitizeFloatingWidth(ToContentWidth(Width));
        _expandedHeight = SanitizeFloatingHeight(ToContentHeight(Height));
        _expandedLeft = Left;
        _expandedTop = Top;
        ResetFloatingLayout();
        if (normalizedSize)
        {
            SaveSettings();
        }
    }

    private void SaveSettings()
    {
        if (WindowState != WindowState.Normal || _isEdgeDocked || _suppressSettingsSave)
        {
            return;
        }

        _settings.FloatingWindowX = Left + FloatingShadowMargin;
        _settings.FloatingWindowY = Top + FloatingShadowMargin;
        _settings.FloatingWindowWidth = SanitizeFloatingWidth(ToContentWidth(Width));
        _settings.FloatingWindowHeight = SanitizeFloatingHeight(ToContentHeight(Height));
        _expandedWidth = _settings.FloatingWindowWidth;
        _expandedHeight = _settings.FloatingWindowHeight;
        _settingsService.Save(_settings);
    }

    private void TryDockToNearestEdge()
    {
        var workArea = GetWorkingArea();
        var distanceLeft = Math.Abs(Left - workArea.Left);
        var distanceRight = Math.Abs(workArea.Right - (Left + Width));
        var distanceTop = Math.Abs(Top - workArea.Top);
        var distanceBottom = Math.Abs(workArea.Bottom - (Top + Height));

        var isNearLeft = distanceLeft <= EdgeSnapThreshold;
        var isNearRight = distanceRight <= EdgeSnapThreshold;
        var isNearTop = distanceTop <= EdgeSnapThreshold;
        var isNearBottom = distanceBottom <= EdgeSnapThreshold;
        if (!isNearLeft && !isNearRight && !isNearTop && !isNearBottom)
        {
            return;
        }

        var edge = isNearLeft || isNearRight
            ? distanceLeft <= distanceRight ? DockEdge.Left : DockEdge.Right
            : distanceTop <= distanceBottom ? DockEdge.Top : DockEdge.Bottom;

        DockToEdge(edge);
    }

    private void DockToEdge(DockEdge edge)
    {
        var iconLeft = Left + (Width - EdgeIconSize) / 2;
        var iconTop = Top + (Height - EdgeIconSize) / 2;

        if (!_isEdgeDocked)
        {
            _expandedWidth = SanitizeFloatingWidth(ToContentWidth(Width));
            _expandedHeight = SanitizeFloatingHeight(ToContentHeight(Height));
            _expandedLeft = Left;
            _expandedTop = Top;
        }

        _dockEdge = edge;
        _isEdgeDocked = true;
        _isEdgePreviewOpen = false;
        ResizeMode = ResizeMode.NoResize;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        MinWidth = EdgeIconSize;
        MinHeight = EdgeIconSize;
        Width = EdgeIconSize;
        Height = EdgeIconSize;
        ExpandedContent.Visibility = Visibility.Collapsed;
        EdgeIconHost.Visibility = Visibility.Visible;
        ResetEdgeIconLayout();

        var workArea = GetWorkingArea();
        switch (edge)
        {
            case DockEdge.Left:
                Left = workArea.Left - (EdgeIconSize - EdgePeekSize);
                Top = Clamp(iconTop, workArea.Top + 8, workArea.Bottom - EdgeIconSize - 8);
                break;
            case DockEdge.Right:
                Left = workArea.Right - EdgePeekSize;
                Top = Clamp(iconTop, workArea.Top + 8, workArea.Bottom - EdgeIconSize - 8);
                break;
            case DockEdge.Top:
                Left = Clamp(iconLeft, workArea.Left + 8, workArea.Right - EdgeIconSize - 8);
                Top = workArea.Top - (EdgeIconSize - EdgePeekSize);
                break;
            case DockEdge.Bottom:
                Left = Clamp(iconLeft, workArea.Left + 8, workArea.Right - EdgeIconSize - 8);
                Top = workArea.Bottom - EdgePeekSize;
                break;
        }

        _edgeIconLeft = Left;
        _edgeIconTop = Top;
    }

    private void RestoreEdgeIconToFloating(System.Windows.Point mouseScreenPoint)
    {
        _suppressSettingsSave = true;
        try
        {
            _isEdgeDocked = false;
            _isEdgePreviewOpen = false;
            _dockEdge = DockEdge.None;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = ToWindowWidth(MinFloatingWidth);
            MinHeight = ToWindowHeight(MinFloatingHeight);
            MaxWidth = ToWindowWidth(MaxFloatingWidth);
            MaxHeight = ToWindowHeight(MaxFloatingHeight);
            ExpandedContent.Visibility = Visibility.Visible;
            EdgeIconHost.Visibility = Visibility.Collapsed;
            ResetFloatingLayout();
            Width = ToWindowWidth(SanitizeFloatingWidth(_expandedWidth));
            Height = ToWindowHeight(SanitizeFloatingHeight(_expandedHeight));
            PositionFloatingNearMouse(mouseScreenPoint);
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    private void RestoreEdgeDockToExpanded()
    {
        _suppressSettingsSave = true;
        try
        {
            Root.ReleaseMouseCapture();
            _isDraggingEdgeIcon = false;
            _edgeIconDetachedDuringDrag = false;
            _isEdgeDocked = false;
            _isEdgePreviewOpen = false;
            _dockEdge = DockEdge.None;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = ToWindowWidth(MinFloatingWidth);
            MinHeight = ToWindowHeight(MinFloatingHeight);
            MaxWidth = ToWindowWidth(MaxFloatingWidth);
            MaxHeight = ToWindowHeight(MaxFloatingHeight);
            ExpandedContent.Visibility = Visibility.Visible;
            EdgeIconHost.Visibility = Visibility.Collapsed;
            ResetFloatingLayout();
            Width = ToWindowWidth(SanitizeFloatingWidth(_expandedWidth));
            Height = ToWindowHeight(SanitizeFloatingHeight(_expandedHeight));

            var workArea = GetWorkingArea();
            Left = Clamp(_expandedLeft, workArea.Left, workArea.Right - Width);
            Top = Clamp(_expandedTop, workArea.Top, workArea.Bottom - Height);
        }
        finally
        {
            _suppressSettingsSave = false;
        }

        SaveSettings();
    }

    private void FinishEdgeIconDrag()
    {
        Root.ReleaseMouseCapture();

        if (_edgeIconDetachedDuringDrag)
        {
            SaveSettings();
        }
        else
        {
            DockIconAtCurrentPosition();
        }

        _isDraggingEdgeIcon = false;
        _edgeIconDetachedDuringDrag = false;

        if (_isEdgeDocked && IsCursorInsideWindow())
        {
            ShowEdgePreview();
        }
    }

    private bool ShouldDetachEdgeIcon(System.Windows.Point mouseScreenPoint)
    {
        var deltaX = mouseScreenPoint.X - _edgeDragStartMouse.X;
        var deltaY = mouseScreenPoint.Y - _edgeDragStartMouse.Y;
        var absX = Math.Abs(deltaX);
        var absY = Math.Abs(deltaY);
        return _dockEdge switch
        {
            DockEdge.Left => deltaX > EdgeDetachDragDistance && absX > absY * EdgeDetachDirectionRatio,
            DockEdge.Right => deltaX < -EdgeDetachDragDistance && absX > absY * EdgeDetachDirectionRatio,
            DockEdge.Top => deltaY > EdgeDetachDragDistance && absY > absX * EdgeDetachDirectionRatio,
            DockEdge.Bottom => deltaY < -EdgeDetachDragDistance && absY > absX * EdgeDetachDirectionRatio,
            _ => false
        };
    }

    private void MoveEdgeIconWithMouse(System.Windows.Point mouseScreenPoint)
    {
        var workArea = GetWorkingArea();
        switch (_dockEdge)
        {
            case DockEdge.Left:
                Left = workArea.Left - (EdgeIconSize - EdgePeekSize);
                Top = Clamp(mouseScreenPoint.Y - _edgeDragOffsetY, workArea.Top + 8, workArea.Bottom - EdgeIconSize - 8);
                break;
            case DockEdge.Right:
                Left = workArea.Right - EdgePeekSize;
                Top = Clamp(mouseScreenPoint.Y - _edgeDragOffsetY, workArea.Top + 8, workArea.Bottom - EdgeIconSize - 8);
                break;
            case DockEdge.Top:
                Left = Clamp(mouseScreenPoint.X - _edgeDragOffsetX, workArea.Left + 8, workArea.Right - EdgeIconSize - 8);
                Top = workArea.Top - (EdgeIconSize - EdgePeekSize);
                break;
            case DockEdge.Bottom:
                Left = Clamp(mouseScreenPoint.X - _edgeDragOffsetX, workArea.Left + 8, workArea.Right - EdgeIconSize - 8);
                Top = workArea.Bottom - EdgePeekSize;
                break;
            default:
                Left = Clamp(_edgeDragStartLeft + mouseScreenPoint.X - _edgeDragStartMouse.X, workArea.Left - (EdgeIconSize - EdgePeekSize), workArea.Right - EdgePeekSize);
                Top = Clamp(_edgeDragStartTop + mouseScreenPoint.Y - _edgeDragStartMouse.Y, workArea.Top - (EdgeIconSize - EdgePeekSize), workArea.Bottom - EdgePeekSize);
                break;
        }
    }

    private void MoveFloatingWithMouse(System.Windows.Point mouseScreenPoint)
    {
        var workArea = GetWorkingArea();
        var nextLeft = mouseScreenPoint.X - _floatingDragOffsetX;
        var nextTop = mouseScreenPoint.Y - _floatingDragOffsetY;
        Left = Clamp(nextLeft, workArea.Left, workArea.Right - Width);
        Top = Clamp(nextTop, workArea.Top, workArea.Bottom - Height);
    }

    private void DockIconAtCurrentPosition()
    {
        var workArea = GetWorkingArea();
        var edge = _dockEdge == DockEdge.None ? GetNearestEdge(workArea) : _dockEdge;

        _dockEdge = edge;
        _isEdgeDocked = true;
        _isEdgePreviewOpen = false;
        ResizeMode = ResizeMode.NoResize;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        MinWidth = EdgeIconSize;
        MinHeight = EdgeIconSize;
        Width = EdgeIconSize;
        Height = EdgeIconSize;
        ExpandedContent.Visibility = Visibility.Collapsed;
        EdgeIconHost.Visibility = Visibility.Visible;
        ResetEdgeIconLayout();

        switch (edge)
        {
            case DockEdge.Left:
                Left = workArea.Left - (EdgeIconSize - EdgePeekSize);
                Top = Clamp(Top, workArea.Top + 8, workArea.Bottom - EdgeIconSize - 8);
                break;
            case DockEdge.Right:
                Left = workArea.Right - EdgePeekSize;
                Top = Clamp(Top, workArea.Top + 8, workArea.Bottom - EdgeIconSize - 8);
                break;
            case DockEdge.Top:
                Left = Clamp(Left, workArea.Left + 8, workArea.Right - EdgeIconSize - 8);
                Top = workArea.Top - (EdgeIconSize - EdgePeekSize);
                break;
            case DockEdge.Bottom:
                Left = Clamp(Left, workArea.Left + 8, workArea.Right - EdgeIconSize - 8);
                Top = workArea.Bottom - EdgePeekSize;
                break;
        }

        _edgeIconLeft = Left;
        _edgeIconTop = Top;
    }

    private DockEdge GetNearestEdge(Rect workArea)
    {
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        var distanceLeft = Math.Abs(centerX - workArea.Left);
        var distanceRight = Math.Abs(workArea.Right - centerX);
        var distanceTop = Math.Abs(centerY - workArea.Top);
        var distanceBottom = Math.Abs(workArea.Bottom - centerY);
        var nearestDistance = new[] { distanceLeft, distanceRight, distanceTop, distanceBottom }.Min();
        return nearestDistance == distanceLeft
            ? DockEdge.Left
            : nearestDistance == distanceRight
                ? DockEdge.Right
                : nearestDistance == distanceTop
                    ? DockEdge.Top
                    : DockEdge.Bottom;
    }

    private void ShowEdgePreview()
    {
        if (!_isEdgeDocked || _isEdgePreviewOpen)
        {
            return;
        }

        _suppressSettingsSave = true;
        try
        {
            _isEdgePreviewOpen = true;
            ResizeMode = ResizeMode.NoResize;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;

            var previewWidth = SanitizeFloatingWidth(_expandedWidth);
            var previewHeight = SanitizeFloatingHeight(_expandedHeight);
            ExpandedContent.Width = previewWidth;
            ExpandedContent.Height = previewHeight;
            ExpandedContent.Visibility = Visibility.Visible;
            EdgeIconHost.Visibility = Visibility.Visible;

            var workArea = GetWorkingArea();
            switch (_dockEdge)
            {
                case DockEdge.Left:
                    Width = EdgeIconSize + EdgePreviewGap + previewWidth;
                    Height = Math.Max(EdgeIconSize, previewHeight);
                    Left = _edgeIconLeft;
                    Top = Clamp(_edgeIconTop + (EdgeIconSize - Height) / 2, workArea.Top + 8, workArea.Bottom - Height - 8);
                    PositionChild(EdgeIconHost, 0, _edgeIconTop - Top);
                    PositionChild(ExpandedContent, EdgeIconSize + EdgePreviewGap, (Height - previewHeight) / 2);
                    break;
                case DockEdge.Right:
                    Width = EdgeIconSize + EdgePreviewGap + previewWidth;
                    Height = Math.Max(EdgeIconSize, previewHeight);
                    Left = _edgeIconLeft - previewWidth - EdgePreviewGap;
                    Top = Clamp(_edgeIconTop + (EdgeIconSize - Height) / 2, workArea.Top + 8, workArea.Bottom - Height - 8);
                    PositionChild(ExpandedContent, 0, (Height - previewHeight) / 2);
                    PositionChild(EdgeIconHost, previewWidth + EdgePreviewGap, _edgeIconTop - Top);
                    break;
                case DockEdge.Top:
                    Width = Math.Max(EdgeIconSize, previewWidth);
                    Height = EdgeIconSize + EdgePreviewGap + previewHeight;
                    Left = Clamp(_edgeIconLeft + (EdgeIconSize - Width) / 2, workArea.Left + 8, workArea.Right - Width - 8);
                    Top = _edgeIconTop;
                    PositionChild(EdgeIconHost, _edgeIconLeft - Left, 0);
                    PositionChild(ExpandedContent, (Width - previewWidth) / 2, EdgeIconSize + EdgePreviewGap);
                    break;
                case DockEdge.Bottom:
                    Width = Math.Max(EdgeIconSize, previewWidth);
                    Height = EdgeIconSize + EdgePreviewGap + previewHeight;
                    Left = Clamp(_edgeIconLeft + (EdgeIconSize - Width) / 2, workArea.Left + 8, workArea.Right - Width - 8);
                    Top = _edgeIconTop - previewHeight - EdgePreviewGap;
                    PositionChild(ExpandedContent, (Width - previewWidth) / 2, 0);
                    PositionChild(EdgeIconHost, _edgeIconLeft - Left, previewHeight + EdgePreviewGap);
                    break;
            }
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    private void ReturnToEdgeIcon()
    {
        if (!_isEdgeDocked)
        {
            return;
        }

        _suppressSettingsSave = true;
        try
        {
            _isEdgePreviewOpen = false;
            ResizeMode = ResizeMode.NoResize;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            MinWidth = EdgeIconSize;
            MinHeight = EdgeIconSize;
            Width = EdgeIconSize;
            Height = EdgeIconSize;
            Left = _edgeIconLeft;
            Top = _edgeIconTop;
            ExpandedContent.Visibility = Visibility.Collapsed;
            EdgeIconHost.Visibility = Visibility.Visible;
            ResetEdgeIconLayout();
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    private bool NormalizeLegacyDefaultSize()
    {
        var isFirstLargeDefault =
            Math.Abs(_settings.FloatingWindowWidth - 360) < 0.1 &&
            Math.Abs(_settings.FloatingWindowHeight - 640) < 0.1;
        var isSecondLargeDefault =
            Math.Abs(_settings.FloatingWindowWidth - 340) < 0.1 &&
            Math.Abs(_settings.FloatingWindowHeight - 460) < 0.1;
        var isThirdLargeDefault =
            Math.Abs(_settings.FloatingWindowWidth - 300) < 0.1 &&
            Math.Abs(_settings.FloatingWindowHeight - 390) < 0.1;
        var isPreviousCompactDefault =
            Math.Abs(_settings.FloatingWindowWidth - 280) < 0.1 &&
            Math.Abs(_settings.FloatingWindowHeight - 360) < 0.1;
        var isAbnormalSavedSize =
            _settings.FloatingWindowWidth < MinFloatingWidth ||
            _settings.FloatingWindowHeight < MinFloatingHeight ||
            _settings.FloatingWindowWidth > MaxFloatingWidth ||
            _settings.FloatingWindowHeight > MaxFloatingHeight;
        if (isFirstLargeDefault || isSecondLargeDefault || isThirdLargeDefault || isPreviousCompactDefault || isAbnormalSavedSize)
        {
            _settings.FloatingWindowWidth = DefaultFloatingWidth;
            _settings.FloatingWindowHeight = DefaultFloatingHeight;
            return true;
        }

        return false;
    }

    private void ClampExpandedSizeToWorkArea()
    {
        var workArea = GetWorkingArea();
        var contentWidth = Math.Min(
            SanitizeFloatingWidth(ToContentWidth(Width)),
            Math.Max(MinFloatingWidth, workArea.Width - 32 - FloatingShadowInset));
        var contentHeight = Math.Min(
            SanitizeFloatingHeight(ToContentHeight(Height)),
            Math.Max(MinFloatingHeight, workArea.Height - 32 - FloatingShadowInset));

        Width = ToWindowWidth(contentWidth);
        Height = ToWindowHeight(contentHeight);
    }

    private static double SanitizeFloatingWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width))
        {
            return DefaultFloatingWidth;
        }

        return Clamp(width, MinFloatingWidth, MaxFloatingWidth);
    }

    private static double SanitizeFloatingHeight(double height)
    {
        if (double.IsNaN(height) || double.IsInfinity(height))
        {
            return DefaultFloatingHeight;
        }

        return Clamp(height, MinFloatingHeight, MaxFloatingHeight);
    }

    private static double ToWindowWidth(double contentWidth)
    {
        return contentWidth + FloatingShadowInset;
    }

    private static double ToWindowHeight(double contentHeight)
    {
        return contentHeight + FloatingShadowInset;
    }

    private static double ToContentWidth(double windowWidth)
    {
        return Math.Max(0, windowWidth - FloatingShadowInset);
    }

    private static double ToContentHeight(double windowHeight)
    {
        return Math.Max(0, windowHeight - FloatingShadowInset);
    }

    private System.Windows.Point GetMouseScreenPoint(System.Windows.Point localPoint)
    {
        var cursorPosition = Forms.Cursor.Position;
        var screenPoint = new System.Windows.Point(cursorPosition.X, cursorPosition.Y);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            screenPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
        }

        return screenPoint;
    }

    private bool IsCursorInsideWindow()
    {
        var cursorPosition = GetMouseScreenPoint(new System.Windows.Point());
        return cursorPosition.X >= Left &&
               cursorPosition.X <= Left + Width &&
               cursorPosition.Y >= Top &&
               cursorPosition.Y <= Top + Height;
    }

    private static void PositionChild(FrameworkElement element, double left, double top)
    {
        element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        element.VerticalAlignment = VerticalAlignment.Top;
        element.Margin = new Thickness(left, top, 0, 0);
    }

    private void PositionFloatingNearMouse(System.Windows.Point mouseScreenPoint)
    {
        var workArea = GetWorkingArea();
        var desiredLeft = mouseScreenPoint.X - Width / 2;
        var desiredTop = mouseScreenPoint.Y - FloatingShadowMargin - 34;
        Left = Clamp(desiredLeft, workArea.Left + EdgeDetachInset, workArea.Right - Width - EdgeDetachInset);
        Top = Clamp(desiredTop, workArea.Top + EdgeDetachInset, workArea.Bottom - Height - EdgeDetachInset);
    }

    private void ResetFloatingLayout()
    {
        ExpandedContent.Width = double.NaN;
        ExpandedContent.Height = double.NaN;
        ExpandedContent.Margin = new Thickness(FloatingShadowMargin);
        ExpandedContent.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        ExpandedContent.VerticalAlignment = VerticalAlignment.Stretch;
        EdgeIconHost.Margin = new Thickness(0);
        EdgeIconHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        EdgeIconHost.VerticalAlignment = VerticalAlignment.Center;
    }

    private void ResetEdgeIconLayout()
    {
        ExpandedContent.Margin = new Thickness(0);
        ExpandedContent.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        ExpandedContent.VerticalAlignment = VerticalAlignment.Stretch;
        EdgeIconHost.Margin = new Thickness(0);
        EdgeIconHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        EdgeIconHost.VerticalAlignment = VerticalAlignment.Center;
    }

    private Rect GetWorkingArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var area = handle == IntPtr.Zero
            ? Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens[0].WorkingArea
            : Forms.Screen.FromHandle(handle).WorkingArea;
        var topLeft = new System.Windows.Point(area.Left, area.Top);
        var bottomRight = new System.Windows.Point(area.Right, area.Bottom);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            topLeft = source.CompositionTarget.TransformFromDevice.Transform(topLeft);
            bottomRight = source.CompositionTarget.TransformFromDevice.Transform(bottomRight);
        }

        return new Rect(topLeft, bottomRight);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private static FloatingTaskItemViewModel? GetFloatingTask(DependencyObject? source)
    {
        return FindFloatingTaskHost(source)?.Tag as FloatingTaskItemViewModel;
    }

    private static FrameworkElement? FindFloatingTaskHost(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: FloatingTaskItemViewModel } element)
            {
                return element;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool CanReorder(FloatingTaskItemViewModel? dragged, FloatingTaskItemViewModel? target)
    {
        if (dragged is null || target is null || dragged.TaskId == target.TaskId)
        {
            return false;
        }

        if (dragged.Status != TodoStatus.Active || target.Status != TodoStatus.Active)
        {
            return false;
        }

        if (dragged.IsSubTask != target.IsSubTask)
        {
            return false;
        }

        return !dragged.IsSubTask || dragged.ParentTaskId == target.ParentTaskId;
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        return FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
               FindAncestor<System.Windows.Controls.TextBox>(source) is not null ||
               FindAncestor<System.Windows.Controls.ComboBox>(source) is not null;
    }

    private static string T(string key) => LocalizationService.Text(key);

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private enum DockEdge
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }
}
