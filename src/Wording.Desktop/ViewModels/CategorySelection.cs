using System.Collections.ObjectModel;

namespace Wording.Desktop.ViewModels;

public sealed class CategoryChoice : ObservableObject
{
    private bool isSelected;
    public string Name { get; }
    public bool IsAll { get; }
    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            // ToggleButton changes its target value before invoking the command, even in OneWay mode.
            if (!SetProperty(ref isSelected, value)) OnPropertyChanged();
        }
    }
    public RelayCommand ToggleCommand { get; }
    public CategoryChoice(string name, bool isAll, Action<CategoryChoice> toggle)
    { Name = name; IsAll = isAll; ToggleCommand = new(_ => toggle(this)); }
}

public sealed class CategorySelection : ObservableObject
{
    public ObservableCollection<CategoryChoice> Choices { get; } = [];
    public string[] SelectedNames => Choices.Where(x => !x.IsAll && x.IsSelected).Select(x => x.Name).ToArray();
    public event Action? Changed;
    public CategorySelection(string allLabel = "全部顯示") => Choices.Add(new(allLabel, true, Toggle) { IsSelected = true });

    public void SetAvailable(IEnumerable<string> names)
    {
        var selected = SelectedNames;
        var available = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (Choices.Skip(1).Select(x => x.Name).SequenceEqual(available)) return;
        while (Choices.Count > 1) Choices.RemoveAt(Choices.Count - 1);
        foreach (var name in available) Choices.Add(new(name, false, Toggle));
        SetSelected(selected);
    }

    public void SetSelected(IEnumerable<string> names)
    {
        var selected = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in Choices.Where(x => !x.IsAll)) choice.IsSelected = selected.Contains(choice.Name);
        Choices[0].IsSelected = SelectedNames.Length == 0;
        OnPropertyChanged(nameof(SelectedNames));
    }

    private void Toggle(CategoryChoice choice)
    {
        if (choice.IsAll && choice.IsSelected) { SetSelected([]); return; }
        SetSelected(choice.IsAll ? [] : choice.IsSelected ? SelectedNames.Where(x => x != choice.Name) : [.. SelectedNames, choice.Name]);
        Changed?.Invoke();
    }
}
