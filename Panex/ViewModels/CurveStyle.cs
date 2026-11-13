// File: \ViewModels\CurveStyle.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Panex.ViewModels
{
    public partial class CurveStyle : ObservableObject
    {
        private readonly Action _repaint;

        // Default states for resetting
        private readonly Windows.UI.Color _defaultColor;
        private readonly ScottPlot.LinePattern _defaultPattern;
        private readonly double _defaultLineWidth;

        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private Windows.UI.Color uiColor;

        [ObservableProperty]
        private ScottPlot.LinePattern selectedPattern;

        [ObservableProperty]
        private double lineWidth;

        // Valid ScottPlot 5 Line Patterns
        public ScottPlot.LinePattern[] AvailablePatterns { get; } = new[]
        {
            ScottPlot.LinePattern.Solid,
            ScottPlot.LinePattern.Dashed,
            ScottPlot.LinePattern.Dotted,
            ScottPlot.LinePattern.DenselyDashed,
        };

        // Helper property to safely translate Windows.UI.Color to ScottPlot.Color
        public ScottPlot.Color SpColor => ScottPlot.Color.FromARGB((uint)((UiColor.A << 24) | (UiColor.R << 16) | (UiColor.G << 8) | UiColor.B));

        public CurveStyle(string name, Windows.UI.Color defaultColor, ScottPlot.LinePattern defaultPattern, Action repaint)
        {
            Name = name;
            _repaint = repaint;

            // Store the initial baseline defaults so they can be restored later
            _defaultColor = defaultColor;
            _defaultPattern = defaultPattern;

            // CHANGE THIS TO 2.0
            _defaultLineWidth = 2.0;

            // Initialize the active properties
            UiColor = defaultColor;
            SelectedPattern = defaultPattern;
            LineWidth = _defaultLineWidth;
        }

        public void ResetToDefaults()
        {
            UiColor = _defaultColor;
            SelectedPattern = _defaultPattern;
            LineWidth = _defaultLineWidth;
        }

        // Trigger the debounce timer whenever a visual property changes
        partial void OnUiColorChanged(Windows.UI.Color value) => _repaint?.Invoke();
        partial void OnSelectedPatternChanged(ScottPlot.LinePattern value) => _repaint?.Invoke();
        partial void OnLineWidthChanged(double value) => _repaint?.Invoke();
    }
}