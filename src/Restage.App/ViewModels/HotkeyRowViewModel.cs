namespace Restage.ViewModels;

/// <summary>A workspace's global hotkey as shown and recorded in the details pane.</summary>
public sealed class HotkeyRowViewModel : ViewModelBase
{
    public HotkeyRowViewModel(string action, string text)
    {
        Action = action;
        _text = text;
    }

    /// <summary>Key into AppSettings.Hotkeys.</summary>
    public string Action { get; }

    string _text;
    /// <summary>The bound combination ("Ctrl+Alt+R"), or empty.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value)) return;
            OnPropertyChanged(nameof(HasHotkey));
            OnPropertyChanged(nameof(Display));
        }
    }

    public bool HasHotkey => Text.Length > 0;

    bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set { if (SetProperty(ref _isRecording, value)) { Partial = ""; OnPropertyChanged(nameof(Display)); } }
    }

    string _partial = "";
    /// <summary>Modifiers held so far while recording ("Ctrl+Alt+").</summary>
    public string Partial
    {
        get => _partial;
        set { if (SetProperty(ref _partial, value)) OnPropertyChanged(nameof(Display)); }
    }

    /// <summary>What the recorder box shows.</summary>
    public string Display => IsRecording
        ? (Partial.Length > 0 ? Partial : "Press a key combination")
        : HasHotkey ? Text : "Add a shortcut";

    string _problem = "";
    /// <summary>Why the combination does not work (taken by another app, already used here), or empty.</summary>
    public string Problem
    {
        get => _problem;
        set { if (SetProperty(ref _problem, value)) OnPropertyChanged(nameof(HasProblem)); }
    }

    public bool HasProblem => Problem.Length > 0;
}
