// File: \ViewModels\SelectableItem.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Panex.ViewModels;

public partial class SelectableItem : ObservableObject
{
    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private bool isSelected = false;

    // Identifiers used to look up data
    public int Z { get; set; }
    public int A { get; set; }
    public int SubshellIndex { get; set; }

    // Flags to know what type of item this is
    public bool IsIsotope { get; set; }
    public bool IsSubshell { get; set; }

    // Action to trigger updates in the ViewModel
    private readonly Action? _onChanged;

    public SelectableItem(string name, Action? onChanged)
    {
        Name = name;
        _onChanged = onChanged;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _onChanged?.Invoke();
    }
}