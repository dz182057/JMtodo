using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.Models;

public sealed class ImportTaskPreview : INotifyPropertyChanged
{
    private DateOnly _startDate = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly? _dueDate;
    private string _title = string.Empty;
    private string? _note;
    private string? _groupName;
    private string _startDateInput = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
    private string _dueDateInput = string.Empty;
    private bool _isSelected = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string? Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value))
            {
                OnPropertyChanged(nameof(NoteText));
            }
        }
    }

    public DateOnly StartDate
    {
        get => DateOnly.TryParse(StartDateInput, out var date) ? date : _startDate;
        set
        {
            _startDate = value;
            StartDateInput = value.ToString("yyyy-MM-dd");
        }
    }

    public DateOnly? DueDate
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DueDateInput))
            {
                return null;
            }

            return DateOnly.TryParse(DueDateInput, out var date) ? date : _dueDate;
        }
        set
        {
            _dueDate = value;
            DueDateInput = value?.ToString("yyyy-MM-dd") ?? string.Empty;
        }
    }

    public string? GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value))
            {
                OnPropertyChanged(nameof(GroupText));
            }
        }
    }

    public string GroupIconKey { get; set; } = "folder";

    public string? GroupDescription { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
            {
                return;
            }

            foreach (var subtask in Subtasks)
            {
                subtask.IsSelected = value;
            }
        }
    }

    public bool IsSubtask { get; set; }

    public bool CanEditGroup => !IsSubtask;

    public string StartDateInput
    {
        get => _startDateInput;
        set
        {
            if (SetProperty(ref _startDateInput, value))
            {
                OnPropertyChanged(nameof(StartDateText));
                OnPropertyChanged(nameof(StartDateDisplayText));
            }
        }
    }

    public string DueDateInput
    {
        get => _dueDateInput;
        set
        {
            if (SetProperty(ref _dueDateInput, value))
            {
                OnPropertyChanged(nameof(DueDateText));
                OnPropertyChanged(nameof(DueDateDisplayText));
            }
        }
    }

    public ObservableCollection<ImportAttachmentPreview> Attachments { get; } = new();

    public ObservableCollection<ImportTaskPreview> Subtasks { get; } = new();

    public string StartDateText => StartDate.ToString("yyyy-MM-dd");

    public string DueDateText => DueDate?.ToString("yyyy-MM-dd") ?? LocalizationService.Text("Todo.NoDue");

    public string StartDateDisplayText => LocalizationService.Format("Import.StartDateFormat", StartDateText);

    public string DueDateDisplayText => LocalizationService.Format("Import.DueDateFormat", DueDateText);

    public string GroupText => string.IsNullOrWhiteSpace(GroupName)
        ? LocalizationService.Text("Group.NoGroup")
        : GroupName;

    public string NoteText => string.IsNullOrWhiteSpace(Note)
        ? LocalizationService.Text("Import.NoNote")
        : Note;

    public string AttachmentCountText => Attachments.Count == 0
        ? LocalizationService.Text("Attachment.None")
        : LocalizationService.Format("Attachment.CountFormat", Attachments.Count);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
