using System.Windows.Media;
using Rewake.Services;

namespace Rewake.ViewModels;

/// <summary>One captured Explorer folder; selectable for restore.</summary>
public class FolderItemViewModel : ViewModelBase
{
    public FolderItemViewModel(string path) => Path = path;

    public string Path { get; }

    /// <summary>Folder name, falling back to the full path for drive roots.</summary>
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Path;
    public string Parent => System.IO.Path.GetDirectoryName(Path.TrimEnd('\\', '/')) ?? Path;
    public ImageSource? Icon => IconCache.GetFolderIcon();

    bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
