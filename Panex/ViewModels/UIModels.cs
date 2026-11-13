// File: \ViewModels\UIModels.cs
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;

namespace Panex.ViewModels;

// Replaces the old ColumnDefinitionData for the ListView/ItemsRepeater refactor
public class ColumnHeader
{
    public string Title { get; set; } = string.Empty;
    public double Width { get; set; } = 180; // Fixed width to keep columns aligned
}

// Replaces ExpandoObject to hold the data for the high-performance tables
public class PhysicsTableRow
{
    public string Energy { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
}

public class PreviewRow
{
    public string Symbol { get; set; } = string.Empty;
    public string FractionText { get; set; } = string.Empty;
}

public class HoverItem
{
    public string Label { get; set; } = string.Empty;
    public string ValueString { get; set; } = string.Empty;
    public SolidColorBrush ColorBrush { get; set; } = new(Microsoft.UI.Colors.Transparent);
}

public class AbsorptionEdgeRow
{
    public string Element { get; set; } = string.Empty;
    public string Subshell { get; set; } = string.Empty;
    public double EnergyMeV { get; set; }

    public string EnergyKeVString => (EnergyMeV * 1000.0).ToString("F3");
    public string EnergyMeVString => EnergyMeV.ToString("G6");
}