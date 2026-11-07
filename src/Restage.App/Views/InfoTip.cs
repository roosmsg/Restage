namespace Restage.Views;

/// <summary>
/// A tooltip with a heading and an explanation, drawn by the InfoTip template
/// in App.xaml: used on the details pane's rows so each icon says what it shows.
/// </summary>
public sealed class InfoTip
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}
