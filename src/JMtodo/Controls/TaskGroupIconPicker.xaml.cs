using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.Windows.Shapes.Path;

namespace TodoDesktopApp.Controls;

public partial class TaskGroupIconPicker : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SelectedIconKeyProperty =
        DependencyProperty.Register(
            nameof(SelectedIconKey),
            typeof(string),
            typeof(TaskGroupIconPicker),
            new FrameworkPropertyMetadata("folder", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIconKeyChanged));

    public static readonly DependencyProperty IconOptionsProperty =
        DependencyProperty.Register(
            nameof(IconOptions),
            typeof(IEnumerable<TaskGroupIconOption>),
            typeof(TaskGroupIconPicker),
            new PropertyMetadata(TaskGroupIconCatalog.Options, OnIconOptionsChanged));

    public TaskGroupIconPicker()
    {
        InitializeComponent();
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        Unloaded += (_, _) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        RenderOptions();
    }

    public string SelectedIconKey
    {
        get => (string)GetValue(SelectedIconKeyProperty);
        set => SetValue(SelectedIconKeyProperty, value);
    }

    public IEnumerable<TaskGroupIconOption> IconOptions
    {
        get => (IEnumerable<TaskGroupIconOption>)GetValue(IconOptionsProperty);
        set => SetValue(IconOptionsProperty, value);
    }

    private static void OnSelectedIconKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TaskGroupIconPicker)d).RefreshSelection();
    }

    private static void OnIconOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TaskGroupIconPicker)d).RenderOptions();
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        RenderOptions();
    }

    private void RenderOptions()
    {
        if (OptionGrid is null)
        {
            return;
        }

        OptionGrid.Children.Clear();
        foreach (var option in IconOptions ?? TaskGroupIconCatalog.Options)
        {
            var icon = new Path
            {
                Width = 18,
                Height = 18,
                Data = option.Geometry,
                Stroke = option.Foreground,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Width = 38,
                Height = 38,
                Margin = new Thickness(0, 0, 8, 4),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Content = icon,
                Cursor = System.Windows.Input.Cursors.Hand,
                FocusVisualStyle = null,
                Tag = option,
                ToolTip = option.Name
            };
            button.Template = CreateButtonTemplate();
            button.Click += (_, _) =>
            {
                SelectedIconKey = option.Key;
                RefreshSelection();
            };

            OptionGrid.Children.Add(button);
        }

        RefreshSelection();
    }

    private void RefreshSelection()
    {
        if (PreviewBox is null || PreviewIcon is null || OptionGrid is null)
        {
            return;
        }

        var selected = GetSelectedOption();
        PreviewBox.Background = selected.Background;
        PreviewIcon.Data = selected.Geometry;
        PreviewIcon.Stroke = selected.Foreground;

        foreach (var button in OptionGrid.Children.OfType<Button>())
        {
            if (button.Tag is not TaskGroupIconOption option)
            {
                continue;
            }

            var isSelected = option.Key == selected.Key;
            button.Background = isSelected ? option.Background : Brushes.Transparent;
            button.BorderBrush = isSelected ? TaskGroupIconCatalog.Get("folder").Foreground : Brushes.Transparent;
            button.BorderThickness = new Thickness(isSelected ? 2 : 1);
        }
    }

    private TaskGroupIconOption GetSelectedOption()
    {
        return (IconOptions ?? TaskGroupIconCatalog.Options).FirstOrDefault(option => option.Key == SelectedIconKey)
            ?? TaskGroupIconCatalog.Get("folder");
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Root";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F7FF")), "Root"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CFE0FF")), "Root"));
        template.Triggers.Add(hover);
        return template;
    }
}
