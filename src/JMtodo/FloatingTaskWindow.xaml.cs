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
    private const double EdgeSnapThreshold = 48;
    private const double EdgeDetachInset = 36;
    private const double EdgeDetachDragDistance = 40;
    private const double EdgeDetachDirectionRatio = 1.0;
    private const double EdgeIconSize = 56;
    private const double EdgePeekSize = 18;
    private const double EdgePreviewGap = 10;
    private const int CompletionCelebrationDurationMs = 520;
    private const int CompletionExitDelayMs = 330;
    private const int CompletionExitDurationMs = 220;
    private const double CompletionParticleOverlaySize = 192;
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
    private readonly HashSet<string> _runningCompletionAnimations = new();
    private readonly Dictionary<string, System.Windows.Point> _completionAnimationOrigins = new();
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
            var animateCompletion = SystemParameters.ClientAreaAnimation;
            if (animateCompletion)
            {
                _completionAnimationOrigins[todo.TaskId] = GetCompletionClickOrigin(checkBox);
            }

            try
            {
                _viewModel.Complete(todo, animateCompletion);
            }
            catch
            {
                _completionAnimationOrigins.Remove(todo.TaskId);
                throw;
            }
        }
        else
        {
            _completionAnimationOrigins.Remove(todo.TaskId);
            _viewModel.Reopen(todo);
        }
    }

    private System.Windows.Point GetCompletionClickOrigin(System.Windows.Controls.CheckBox checkBox)
    {
        if (checkBox.IsMouseOver)
        {
            return Mouse.GetPosition(Root);
        }

        return checkBox.TransformToVisual(Root).Transform(new System.Windows.Point(
            Math.Max(0, checkBox.ActualWidth) / 2,
            Math.Max(0, checkBox.ActualHeight) / 2));
    }

    private void FloatingTaskItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FloatingTaskItemViewModel todo } item || !todo.IsCompleting)
        {
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            _viewModel.FinishCompletionAnimation(todo.TaskId);
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(() => BeginCompletionAnimation(item, todo)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            if (_isEdgeDocked)
            {
                BeginEdgeIconDrag(e);
                return;
            }

            _dockEdge = DockEdge.None;
            _suppressSettingsSave = true;
            try
            {
                DragMove();
            }
            finally
            {
                _suppressSettingsSave = false;
            }

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
            editor.GetNewAttachmentPaths(),
            editor.IsPinnedOnCreate);
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
            editor.GetNewAttachmentPaths(),
            editor.IsPinnedOnCreate);
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

    private void BeginCompletionAnimation(FrameworkElement item, FloatingTaskItemViewModel todo)
    {
        if (!todo.IsCompleting || !_runningCompletionAnimations.Add(todo.TaskId))
        {
            return;
        }

        item.IsHitTestVisible = false;
        item.ClipToBounds = false;
        item.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        item.RenderTransform = new TranslateTransform();

        var origin = _completionAnimationOrigins.TryGetValue(todo.TaskId, out var clickOrigin)
            ? clickOrigin
            : item.TransformToVisual(Root).Transform(GetFallbackCompletionBurstOrigin(todo));
        _completionAnimationOrigins.Remove(todo.TaskId);

        var celebrationVisual = new CompletionParticleVisual(
            new System.Windows.Point(CompletionParticleOverlaySize / 2, CompletionParticleOverlaySize / 2),
            PickCompletionParticleKind())
        {
            Width = CompletionParticleOverlaySize,
            Height = CompletionParticleOverlaySize
        };
        var celebrationPopup = new Popup
        {
            AllowsTransparency = true,
            Child = celebrationVisual,
            Focusable = false,
            HorizontalOffset = origin.X - CompletionParticleOverlaySize / 2,
            IsHitTestVisible = false,
            Placement = PlacementMode.Relative,
            PlacementTarget = Root,
            StaysOpen = true,
            VerticalOffset = origin.Y - CompletionParticleOverlaySize / 2
        };
        celebrationPopup.IsOpen = true;
        celebrationVisual.Start();

        var storyboard = new Storyboard();
        var exitEase = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fade = new DoubleAnimation(item.Opacity, 0, TimeSpan.FromMilliseconds(CompletionExitDurationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(CompletionExitDelayMs),
            EasingFunction = exitEase
        };
        Storyboard.SetTarget(fade, item);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        var move = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(CompletionExitDurationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(CompletionExitDelayMs),
            EasingFunction = exitEase
        };
        Storyboard.SetTarget(move, item);
        Storyboard.SetTargetProperty(move, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        storyboard.Children.Add(move);

        storyboard.Completed += (_, _) =>
        {
            celebrationPopup.IsOpen = false;
            celebrationPopup.Child = null;

            _runningCompletionAnimations.Remove(todo.TaskId);
            _viewModel.FinishCompletionAnimation(todo.TaskId);
        };
        storyboard.Begin();
    }

    private static System.Windows.Point GetFallbackCompletionBurstOrigin(FloatingTaskItemViewModel todo)
    {
        return todo.IsSubTask
            ? new System.Windows.Point(45, 18)
            : new System.Windows.Point(24, 26);
    }

    private static CompletionParticleKind PickCompletionParticleKind()
    {
        return (CompletionParticleKind)Random.Shared.Next(Enum.GetValues<CompletionParticleKind>().Length);
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

        BeginEdgeIconDrag(e);
    }

    private void BeginEdgeIconDrag(MouseButtonEventArgs e)
    {
        var mouseScreenPoint = GetMouseScreenPoint(e.GetPosition(this));

        if (_isEdgePreviewOpen)
        {
            ReturnToEdgeIcon();
        }

        _isDraggingEdgeIcon = true;
        _edgeIconDetachedDuringDrag = false;
        _edgeDragStartMouse = mouseScreenPoint;
        _edgeDragStartLeft = _edgeIconLeft;
        _edgeDragStartTop = _edgeIconTop;
        _edgeDragOffsetX = _edgeDragStartMouse.X - _edgeIconLeft;
        _edgeDragOffsetY = _edgeDragStartMouse.Y - _edgeIconTop;
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
        var workArea = GetWorkingArea(GetWindowCenter());
        var distanceLeft = Left - workArea.Left;
        var distanceRight = workArea.Right - (Left + Width);
        var distanceTop = Top - workArea.Top;
        var distanceBottom = workArea.Bottom - (Top + Height);

        var nearestEdge = DockEdge.Left;
        var nearestDistance = distanceLeft;
        if (distanceRight < nearestDistance)
        {
            nearestEdge = DockEdge.Right;
            nearestDistance = distanceRight;
        }

        if (distanceTop < nearestDistance)
        {
            nearestEdge = DockEdge.Top;
            nearestDistance = distanceTop;
        }

        if (distanceBottom < nearestDistance)
        {
            nearestEdge = DockEdge.Bottom;
            nearestDistance = distanceBottom;
        }

        if (nearestDistance > EdgeSnapThreshold)
        {
            return;
        }

        DockToEdge(nearestEdge);
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

        var workArea = GetWorkingArea(new System.Windows.Point(iconLeft + EdgeIconSize / 2, iconTop + EdgeIconSize / 2));
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

            var workArea = GetWorkingArea(GetExpandedWindowCenter());
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
            TryDockToNearestEdge();
            if (!_isEdgeDocked)
            {
                SaveSettings();
            }
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
        var workArea = GetWorkingArea(mouseScreenPoint);
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
        var workArea = GetWorkingArea(mouseScreenPoint);
        var nextLeft = mouseScreenPoint.X - _floatingDragOffsetX;
        var nextTop = mouseScreenPoint.Y - _floatingDragOffsetY;
        Left = Clamp(nextLeft, workArea.Left, workArea.Right - Width);
        Top = Clamp(nextTop, workArea.Top, workArea.Bottom - Height);
    }

    private void DockIconAtCurrentPosition()
    {
        var workArea = GetWorkingArea(GetWindowCenter());
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
        var workArea = GetWorkingArea(mouseScreenPoint);
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
        return GetWorkingArea(GetWindowCenter());
    }

    private Rect GetWorkingArea(System.Windows.Point screenPoint)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var area = handle == IntPtr.Zero
            ? Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens[0].WorkingArea
            : Forms.Screen.FromPoint(ToDevicePoint(screenPoint)).WorkingArea;
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

    private System.Windows.Point GetWindowCenter()
    {
        return new System.Windows.Point(Left + Width / 2, Top + Height / 2);
    }

    private System.Windows.Point GetExpandedWindowCenter()
    {
        return new System.Windows.Point(_expandedLeft + Width / 2, _expandedTop + Height / 2);
    }

    private System.Drawing.Point ToDevicePoint(System.Windows.Point screenPoint)
    {
        var devicePoint = screenPoint;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            devicePoint = source.CompositionTarget.TransformToDevice.Transform(devicePoint);
        }

        return new System.Drawing.Point((int)Math.Round(devicePoint.X), (int)Math.Round(devicePoint.Y));
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

    private sealed class CompletionParticleVisual : FrameworkElement
    {
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(CompletionParticleVisual),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly System.Windows.Media.Brush GreenBrush = CreateBrush("#36C275");
        private static readonly System.Windows.Media.Brush SoftGreenBrush = CreateBrush("#DDF4E8");
        private static readonly System.Windows.Media.Brush BlueBrush = CreateBrush("#5B8CFF");
        private static readonly System.Windows.Media.Brush CyanBrush = CreateBrush("#35C6DF");
        private static readonly System.Windows.Media.Brush AmberBrush = CreateBrush("#FFB84D");
        private static readonly System.Windows.Media.Brush GoldBrush = CreateBrush("#FFD15C");
        private static readonly System.Windows.Media.Brush VioletBrush = CreateBrush("#8A74FF");
        private static readonly System.Windows.Media.Brush PinkBrush = CreateBrush("#EE6F9F");
        private static readonly System.Windows.Media.Brush[] CoreBrushes = { GreenBrush, BlueBrush, AmberBrush, VioletBrush };
        private static readonly System.Windows.Media.Brush[] NeonBrushes = { CyanBrush, VioletBrush, PinkBrush, BlueBrush };
        private static readonly System.Windows.Media.Brush[] WarmBrushes = { GoldBrush, AmberBrush, PinkBrush, GreenBrush };

        private readonly System.Windows.Point _origin;
        private readonly CompletionParticleKind _kind;

        public CompletionParticleVisual(System.Windows.Point origin, CompletionParticleKind kind)
        {
            _origin = origin;
            _kind = kind;
            IsHitTestVisible = false;
        }

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public void Start()
        {
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(CompletionCelebrationDurationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(ProgressProperty, animation);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var progress = Clamp01(Progress);
            if (progress <= 0)
            {
                return;
            }

            switch (_kind)
            {
                case CompletionParticleKind.LiftConfetti:
                    DrawLiftConfetti(drawingContext, progress);
                    break;
                case CompletionParticleKind.SpiralFirework:
                    DrawSpiralFirework(drawingContext, progress);
                    break;
                case CompletionParticleKind.StarCluster:
                    DrawStarCluster(drawingContext, progress);
                    break;
                case CompletionParticleKind.NeonSpark:
                    DrawNeonSpark(drawingContext, progress);
                    break;
                case CompletionParticleKind.OrbitNova:
                    DrawOrbitNova(drawingContext, progress);
                    break;
                case CompletionParticleKind.PearlFirework:
                    DrawPearlFirework(drawingContext, progress);
                    break;
                case CompletionParticleKind.PetalNova:
                    DrawPetalNova(drawingContext, progress);
                    break;
                case CompletionParticleKind.SatellitePop:
                    DrawSatellitePop(drawingContext, progress);
                    break;
                case CompletionParticleKind.GalaxySwirl:
                    DrawGalaxySwirl(drawingContext, progress);
                    break;
                case CompletionParticleKind.CandyConfetti:
                    DrawCandyConfetti(drawingContext, progress);
                    break;
                case CompletionParticleKind.TwinNova:
                    DrawTwinNova(drawingContext, progress);
                    break;
                case CompletionParticleKind.HaloParticles:
                    DrawHaloParticles(drawingContext, progress);
                    break;
                case CompletionParticleKind.NeonBloom:
                    DrawNeonBloom(drawingContext, progress);
                    break;
                case CompletionParticleKind.FinaleParticles:
                    DrawFinaleParticles(drawingContext, progress);
                    break;
                case CompletionParticleKind.StardustHalo:
                    DrawStardustHalo(drawingContext, progress);
                    break;
                case CompletionParticleKind.ConfettiOrbit:
                    DrawConfettiOrbit(drawingContext, progress);
                    break;
                case CompletionParticleKind.EmberBloom:
                    DrawEmberBloom(drawingContext, progress);
                    break;
                case CompletionParticleKind.PetalSpiral:
                    DrawPetalSpiral(drawingContext, progress);
                    break;
                case CompletionParticleKind.PrismNova:
                    DrawPrismNova(drawingContext, progress);
                    break;
                default:
                    DrawStarPop(drawingContext, progress);
                    break;
            }
        }

        private void DrawStarPop(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.72);
            var offsets = new[]
            {
                new Vector(-4, -28), new Vector(28, -18), new Vector(34, 8),
                new Vector(7, 30), new Vector(-23, 15)
            };

            for (var i = 0; i < offsets.Length; i++)
            {
                var local = Clamp01((progress - i * 0.05) / 0.78);
                var point = _origin + offsets[i] * travel;
                DrawStar(drawingContext, point, 4 + 5 * Math.Sin(local * Math.PI), i % 2 == 0 ? AmberBrush : BlueBrush, opacity);
            }

            DrawDot(drawingContext, _origin, 4, GreenBrush, opacity);
        }

        private void DrawLiftConfetti(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.7);
            var offsets = new[]
            {
                new Vector(8, -26), new Vector(30, -22), new Vector(48, -4), new Vector(26, 20),
                new Vector(2, 25), new Vector(-14, 12), new Vector(56, -26), new Vector(68, 9)
            };

            for (var i = 0; i < offsets.Length; i++)
            {
                var point = _origin + offsets[i] * travel;
                point.Y -= Math.Sin(progress * Math.PI) * 4;
                DrawShard(drawingContext, point, i * 0.55 + progress * 3, CoreBrushes[i % CoreBrushes.Length], opacity);
            }
        }

        private void DrawSpiralFirework(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.72);
            for (var i = 0; i < 22; i++)
            {
                var angle = i * 0.55 + travel * 2.35;
                var radius = 5 + travel * (13 + i * 1.35);
                var point = PointOnEllipse(_origin, angle, radius, 0.76);
                var brush = CoreBrushes[i % CoreBrushes.Length];
                if (i % 3 == 0)
                {
                    DrawStar(drawingContext, point, 2.4 + 2.8 * Math.Sin(progress * Math.PI), brush, opacity);
                }
                else
                {
                    DrawDot(drawingContext, point, 1.9 + i % 2 * 0.5, brush, opacity);
                }
            }
        }

        private void DrawStarCluster(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.74);
            for (var i = 0; i < 11; i++)
            {
                var angle = Math.PI * 2 * i / 11 + 0.4;
                var radius = 5 + (12 + i % 4 * 5) * travel;
                var point = PointOnEllipse(_origin, angle, radius, 0.82);
                DrawStar(drawingContext, point, 2.4 + 3.2 * Math.Sin(progress * Math.PI), MixedBrush(i), opacity);
            }
        }

        private void DrawNeonSpark(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.72);
            for (var i = 0; i < 12; i++)
            {
                var angle = -Math.PI * 0.9 + Math.PI * 1.8 * i / 11;
                var point = PointOnEllipse(_origin, angle, 12 + 26 * travel, 0.78);
                DrawStar(drawingContext, point, 2.5 + 4 * Math.Sin(progress * Math.PI), NeonBrushes[i % NeonBrushes.Length], opacity);
            }
        }

        private void DrawOrbitNova(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.74);
            for (var i = 0; i < 18; i++)
            {
                var lane = i % 3;
                var angle = i * 0.7 + travel * (1.7 + lane * 0.28);
                var radius = 10 + travel * (18 + lane * 7);
                var point = PointOnEllipse(_origin, angle, radius, 0.72 + lane * 0.08);
                var brush = CoreBrushes[i % CoreBrushes.Length];
                if (i % 4 == 0)
                {
                    DrawStar(drawingContext, point, 2.8 + Math.Sin(progress * Math.PI) * 2.6, brush, opacity);
                }
                else
                {
                    DrawDot(drawingContext, point, 2 + lane * 0.4, brush, opacity);
                }
            }
        }

        private void DrawPearlFirework(DrawingContext drawingContext, double progress)
        {
            DrawParticleBloom(drawingContext, progress, _origin + new Vector(4, -2), 24, 34, new[] { GoldBrush, GreenBrush, BlueBrush, AmberBrush }, ParticleShape.Dot, 0.1);
            DrawDot(drawingContext, _origin + new Vector(4, -2), 7 * (1 - Clamp01(progress - 0.3)), SoftGreenBrush, FadeOut(progress, 0.62) * 0.75);
        }

        private void DrawPetalNova(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.74);
            for (var i = 0; i < 18; i++)
            {
                var angle = Math.PI * 2 * i / 18 + travel * 0.55;
                var point = PointOnEllipse(_origin, angle, 11 + travel * (24 + i % 3 * 5), 0.82);
                DrawPetal(drawingContext, point, angle + progress * 3, MixedBrush(i), opacity);
            }
        }

        private void DrawSatellitePop(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.76);
            for (var i = 0; i < 14; i++)
            {
                var angle = -Math.PI * 0.9 + Math.PI * 1.8 * i / 13;
                var orbit = PointOnEllipse(_origin, angle + travel * 1.2, 12 + travel * (17 + i % 4 * 5), 0.66);
                DrawDot(drawingContext, orbit, 2.4, MixedBrush(i), opacity);
                if (i % 3 == 0)
                {
                    var outer = PointOnEllipse(_origin, angle + travel * 1.2, 20 + travel * (17 + i % 4 * 5), 0.66);
                    DrawStar(drawingContext, outer, 2.6, MixedBrush(i + 1), opacity);
                }
            }
        }

        private void DrawGalaxySwirl(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.75);
            for (var i = 0; i < 30; i++)
            {
                var arm = i % 3;
                var step = i / 3;
                var angle = arm * Math.PI * 2 / 3 + step * 0.42 + travel * 1.4;
                var point = PointOnEllipse(_origin, angle, 4 + travel * (8 + step * 3.6), 0.76);
                DrawDot(drawingContext, point, 1.6 + step % 3 * 0.28, NeonBrushes[i % NeonBrushes.Length], opacity);
            }
        }

        private void DrawCandyConfetti(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.74);
            for (var i = 0; i < 20; i++)
            {
                var angle = Math.PI * 2 * i / 20 + (i % 2 == 0 ? -0.15 : 0.2);
                var point = PointOnEllipse(_origin, angle, 10 + travel * (20 + i % 5 * 5), 0.78);
                DrawRoundShard(drawingContext, point, i * 0.55 + progress * 4, MixedBrush(i), opacity);
            }
        }

        private void DrawTwinNova(DrawingContext drawingContext, double progress)
        {
            DrawParticleBloom(drawingContext, Clamp01(progress / 0.86), _origin + new Vector(-10, 0), 16, 26, new[] { GreenBrush, GoldBrush, BlueBrush }, ParticleShape.Mixed, 0.25);
            var second = Clamp01((progress - 0.18) / 0.78);
            if (second > 0)
            {
                DrawParticleBloom(drawingContext, second, _origin + new Vector(30, -7), 14, 22, new[] { CyanBrush, VioletBrush, PinkBrush }, ParticleShape.Mixed, -0.18);
            }
        }

        private void DrawHaloParticles(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutQuad(progress);
            var opacity = FadeOut(progress, 0.78);
            for (var ringIndex = 0; ringIndex < 2; ringIndex++)
            {
                var count = ringIndex == 0 ? 10 : 14;
                var radius = (ringIndex == 0 ? 13 : 22) + travel * (ringIndex == 0 ? 9 : 12);
                for (var i = 0; i < count; i++)
                {
                    var angle = Math.PI * 2 * i / count + travel * (ringIndex == 0 ? 1.2 : -1.1);
                    var point = PointOnEllipse(_origin, angle, radius, 0.7);
                    DrawDot(drawingContext, point, ringIndex == 0 ? 2.3 : 1.9, MixedBrush(i + ringIndex), opacity * (ringIndex == 0 ? 1 : 0.78));
                }
            }
        }

        private void DrawNeonBloom(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.74);
            for (var i = 0; i < 18; i++)
            {
                var angle = Math.PI * 2 * i / 18 + Math.Sin(travel * Math.PI) * 0.28;
                var point = PointOnEllipse(_origin, angle, 11 + 28 * travel, 0.78);
                if (i % 2 == 0)
                {
                    DrawStar(drawingContext, point, 3.2 + 2.2 * Math.Sin(progress * Math.PI), NeonBrushes[i % NeonBrushes.Length], opacity);
                }
                else
                {
                    DrawDot(drawingContext, point, 2.5, NeonBrushes[i % NeonBrushes.Length], opacity);
                }
            }
        }

        private void DrawFinaleParticles(DrawingContext drawingContext, double progress)
        {
            DrawParticleBloom(drawingContext, progress, _origin + new Vector(2, -1), 28, 38, new[] { GoldBrush, GreenBrush, BlueBrush, PinkBrush, VioletBrush }, ParticleShape.Mixed, 0.32);
            var late = Clamp01((progress - 0.16) / 0.78);
            if (late <= 0)
            {
                return;
            }

            var center = _origin + new Vector(18, -6);
            for (var i = 0; i < 12; i++)
            {
                var angle = Math.PI * 2 * i / 12 - late * 0.8;
                var point = PointOnEllipse(center, angle, 8 + 22 * EaseOutCubic(late), 0.72);
                DrawShard(drawingContext, point, angle + late * 4, MixedBrush(i), FadeOut(late, 0.7));
            }
        }

        private void DrawStardustHalo(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.76);
            for (var layer = 0; layer < 2; layer++)
            {
                var count = layer == 0 ? 14 : 18;
                var baseRadius = layer == 0 ? 15 : 24;
                var drift = layer == 0 ? 1.35 : -1.15;
                for (var i = 0; i < count; i++)
                {
                    var angle = Math.PI * 2 * i / count + travel * drift;
                    var radius = baseRadius + travel * (layer == 0 ? 9 : 13) + Math.Sin(i + travel * 4) * 2;
                    var point = PointOnEllipse(_origin, angle, radius, 0.74);
                    if ((i + layer) % 5 == 0)
                    {
                        DrawStar(drawingContext, point, 2.2 + 2.3 * Math.Sin(progress * Math.PI), MixedBrush(i + layer), opacity);
                    }
                    else
                    {
                        DrawDot(drawingContext, point, layer == 0 ? 2.2 : 1.7, MixedBrush(i + layer), opacity * (layer == 0 ? 1 : 0.72));
                    }
                }
            }
        }

        private void DrawConfettiOrbit(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.76);
            for (var i = 0; i < 18; i++)
            {
                var angle = Math.PI * 2 * i / 18 + travel * 1.25;
                var point = PointOnEllipse(_origin, angle, 13 + travel * (18 + i % 3 * 5), 0.66);
                if (i % 2 == 0)
                {
                    DrawRoundShard(drawingContext, point, angle + travel * 4, MixedBrush(i), opacity);
                }
                else
                {
                    DrawShard(drawingContext, point, angle - travel * 3, MixedBrush(i), opacity);
                }
            }
        }

        private void DrawEmberBloom(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.74);
            for (var i = 0; i < 26; i++)
            {
                var angle = Math.PI * 2 * i / 26 + Math.Sin(travel * Math.PI) * 0.22;
                var lane = i % 4;
                var point = PointOnEllipse(_origin, angle, 7 + travel * (18 + lane * 5), 0.78);
                DrawDot(drawingContext, point, 1.8 + lane * 0.35, WarmBrushes[i % WarmBrushes.Length], opacity);
            }

            for (var i = 0; i < 7; i++)
            {
                var angle = -Math.PI * 0.95 + i * Math.PI * 0.32;
                var point = PointOnEllipse(_origin, angle, 15 + travel * 22, 0.72);
                DrawStar(drawingContext, point, 2.4 + 2.6 * Math.Sin(progress * Math.PI), WarmBrushes[(i + 1) % WarmBrushes.Length], opacity);
            }
        }

        private void DrawPetalSpiral(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.76);
            for (var i = 0; i < 20; i++)
            {
                var angle = i * 0.56 + travel * 1.55;
                var point = PointOnEllipse(_origin, angle, 8 + travel * (12 + i * 1.35), 0.76);
                DrawPetal(drawingContext, point, angle + progress * 3.5, MixedBrush(i), opacity);
            }
        }

        private void DrawPrismNova(DrawingContext drawingContext, double progress)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.74);
            for (var i = 0; i < 22; i++)
            {
                var angle = Math.PI * 2 * i / 22 + (i % 2 == 0 ? -travel * 0.32 : travel * 0.45);
                var point = PointOnEllipse(_origin, angle, 8 + travel * (20 + i % 5 * 4), 0.76);
                var brush = PrismBrush(i);
                if (i % 3 == 0)
                {
                    DrawDiamond(drawingContext, point, 3.5 + Math.Sin(progress * Math.PI) * 1.8, angle + travel * 2, brush, opacity);
                }
                else if (i % 3 == 1)
                {
                    DrawStar(drawingContext, point, 2.5 + Math.Sin(progress * Math.PI) * 2.2, brush, opacity);
                }
                else
                {
                    DrawDot(drawingContext, point, 2.1, brush, opacity);
                }
            }
        }

        private void DrawParticleBloom(
            DrawingContext drawingContext,
            double progress,
            System.Windows.Point center,
            int count,
            double radius,
            IReadOnlyList<System.Windows.Media.Brush> brushes,
            ParticleShape shape,
            double wobble)
        {
            var travel = EaseOutCubic(progress);
            var opacity = FadeOut(progress, 0.72);
            for (var i = 0; i < count; i++)
            {
                var angle = Math.PI * 2 * i / count + wobble * Math.Sin(travel * Math.PI);
                var lane = i % 4;
                var point = PointOnEllipse(center, angle, 8 + travel * (radius + lane * 3), 0.76);
                var brush = brushes[i % brushes.Count];
                if (shape == ParticleShape.Mixed && i % 3 == 0)
                {
                    DrawStar(drawingContext, point, 2.5 + Math.Sin(progress * Math.PI) * 2.2, brush, opacity);
                }
                else if (shape == ParticleShape.Mixed && i % 3 == 1)
                {
                    DrawShard(drawingContext, point, angle + progress * 4, brush, opacity);
                }
                else
                {
                    DrawDot(drawingContext, point, 1.9 + i % 2 * 0.5, brush, opacity);
                }
            }
        }

        private static void DrawDot(DrawingContext drawingContext, System.Windows.Point point, double radius, System.Windows.Media.Brush brush, double opacity)
        {
            drawingContext.PushOpacity(opacity);
            drawingContext.DrawEllipse(brush, null, point, radius, radius);
            drawingContext.Pop();
        }

        private static void DrawStar(DrawingContext drawingContext, System.Windows.Point center, double size, System.Windows.Media.Brush brush, double opacity)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var i = 0; i < 8; i++)
                {
                    var radius = i % 2 == 0 ? size : size * 0.38;
                    var angle = -Math.PI / 2 + i * Math.PI / 4;
                    var point = PointOnEllipse(center, angle, radius);
                    if (i == 0)
                    {
                        context.BeginFigure(point, true, true);
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                }
            }

            geometry.Freeze();
            drawingContext.PushOpacity(opacity);
            drawingContext.DrawGeometry(brush, null, geometry);
            drawingContext.Pop();
        }

        private static void DrawShard(DrawingContext drawingContext, System.Windows.Point point, double angle, System.Windows.Media.Brush brush, double opacity)
        {
            drawingContext.PushOpacity(opacity);
            drawingContext.PushTransform(new TranslateTransform(point.X, point.Y));
            drawingContext.PushTransform(new RotateTransform(ToDegrees(angle)));
            drawingContext.DrawRectangle(brush, null, new Rect(-3.4, -1.6, 6.8, 3.2));
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }

        private static void DrawRoundShard(DrawingContext drawingContext, System.Windows.Point point, double angle, System.Windows.Media.Brush brush, double opacity)
        {
            drawingContext.PushOpacity(opacity);
            drawingContext.PushTransform(new TranslateTransform(point.X, point.Y));
            drawingContext.PushTransform(new RotateTransform(ToDegrees(angle)));
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(-4, -2, 8, 4), 2, 2);
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }

        private static void DrawPetal(DrawingContext drawingContext, System.Windows.Point point, double angle, System.Windows.Media.Brush brush, double opacity)
        {
            drawingContext.PushOpacity(opacity);
            drawingContext.PushTransform(new TranslateTransform(point.X, point.Y));
            drawingContext.PushTransform(new RotateTransform(ToDegrees(angle)));
            drawingContext.DrawEllipse(brush, null, new System.Windows.Point(), 4.2, 2.5);
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }

        private static void DrawDiamond(DrawingContext drawingContext, System.Windows.Point center, double size, double angle, System.Windows.Media.Brush brush, double opacity)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var points = new[]
                {
                    new System.Windows.Point(0, -size),
                    new System.Windows.Point(size * 0.72, 0),
                    new System.Windows.Point(0, size),
                    new System.Windows.Point(-size * 0.72, 0)
                };
                context.BeginFigure(points[0], true, true);
                for (var i = 1; i < points.Length; i++)
                {
                    context.LineTo(points[i], true, false);
                }
            }

            geometry.Freeze();
            drawingContext.PushOpacity(opacity);
            drawingContext.PushTransform(new TranslateTransform(center.X, center.Y));
            drawingContext.PushTransform(new RotateTransform(ToDegrees(angle)));
            drawingContext.DrawGeometry(brush, null, geometry);
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }

        private static System.Windows.Point PointOnEllipse(System.Windows.Point center, double angle, double radius, double scaleY = 1)
        {
            return new System.Windows.Point(
                center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius * scaleY);
        }

        private static double EaseOutCubic(double value)
        {
            var progress = Clamp01(value);
            return 1 - Math.Pow(1 - progress, 3);
        }

        private static double EaseOutQuad(double value)
        {
            var progress = Clamp01(value);
            return 1 - Math.Pow(1 - progress, 2);
        }

        private static double FadeOut(double progress, double start)
        {
            if (progress <= start)
            {
                return 1;
            }

            return Math.Max(0, (1 - progress) / (1 - start));
        }

        private static double Clamp01(double value)
        {
            return Math.Min(Math.Max(value, 0), 1);
        }

        private static double ToDegrees(double radians)
        {
            return radians * 180 / Math.PI;
        }

        private static System.Windows.Media.Brush MixedBrush(int index)
        {
            return CoreBrushes[index % CoreBrushes.Length];
        }

        private static System.Windows.Media.Brush PrismBrush(int index)
        {
            var brushes = new[] { CyanBrush, VioletBrush, GoldBrush, GreenBrush, PinkBrush };
            return brushes[index % brushes.Length];
        }

        private static System.Windows.Media.Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private enum ParticleShape
        {
            Dot,
            Mixed
        }
    }

    private enum CompletionParticleKind
    {
        StarPop,
        LiftConfetti,
        SpiralFirework,
        StarCluster,
        NeonSpark,
        OrbitNova,
        PearlFirework,
        PetalNova,
        SatellitePop,
        GalaxySwirl,
        CandyConfetti,
        TwinNova,
        HaloParticles,
        NeonBloom,
        FinaleParticles,
        StardustHalo,
        ConfettiOrbit,
        EmberBloom,
        PetalSpiral,
        PrismNova
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
