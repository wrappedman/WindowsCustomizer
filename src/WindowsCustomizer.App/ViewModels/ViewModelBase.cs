using CommunityToolkit.Mvvm.ComponentModel;

namespace WindowsCustomizer.App.ViewModels;

/// <summary>Shared surface for all pages.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;
}

/// <summary>UI status reporting channel (implemented by MainViewModel, consumed by page VMs).</summary>
public interface IStatusReporter
{
    void SetStatus(string text);

    void SetProgress(double percent, bool visible = true);
}

/// <summary>One sidebar navigation entry.</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(string key, string title, string glyph, string section, bool isAvailable, string phaseLabel)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        Section = section;
        IsAvailable = isAvailable;
        ToolTip = isAvailable ? title : $"Available in {phaseLabel}";
    }

    public string Key { get; }

    public string Title { get; }

    public string Glyph { get; }

    /// <summary>Group header (PROJECT / CUSTOMIZE / REVIEW / BUILD); empty for ungrouped items.</summary>
    public string Section { get; }

    public bool IsAvailable { get; }

    public string ToolTip { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A sidebar group with its items.</summary>
public sealed class NavSectionViewModel
{
    public NavSectionViewModel(string section, IEnumerable<NavItemViewModel> items)
    {
        Section = section;
        Items = items.ToList();
    }

    public string Section { get; }

    public IReadOnlyList<NavItemViewModel> Items { get; }
}
