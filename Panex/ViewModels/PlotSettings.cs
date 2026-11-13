using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace Panex.ViewModels
{
    public partial class PlotSettings : ObservableObject
    {
        private readonly Action _repaint;

        private readonly bool _defaultIsXLog;
        private readonly bool _defaultIsYLog;
        private bool _defaultShowMinorGrid;

        [ObservableProperty] private bool isXLog;
        [ObservableProperty] private bool isYLog;
        [ObservableProperty] private bool showMajorGrid = true;
        [ObservableProperty] private bool showMinorGrid;

        [ObservableProperty] private bool useCustomXLimits;
        [ObservableProperty] private double xMin = 0.001;
        [ObservableProperty] private double xMax = 100000.0;

        [ObservableProperty] private bool useCustomYLimits;
        [ObservableProperty] private double yMin = 0.0001;
        [ObservableProperty] private double yMax = 10000.0;

        public ObservableCollection<CurveStyle> CurveStyles { get; } = new();

        public PlotSettings(bool isXLog, bool isYLog, Action repaint)
        {
            _repaint = repaint;

            _defaultIsXLog = isXLog;
            _defaultIsYLog = isYLog;

            IsXLog = isXLog;
            IsYLog = isYLog;
        }

        // Capture after initialization so Reset restores this plot's minor grid setting
        public void CaptureDefaults()
        {
            _defaultShowMinorGrid = ShowMinorGrid;
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            IsXLog = _defaultIsXLog;
            IsYLog = _defaultIsYLog;
            ShowMinorGrid = _defaultShowMinorGrid;
            ShowMajorGrid = true;

            UseCustomXLimits = false;
            UseCustomYLimits = false;

            foreach (var style in CurveStyles)
            {
                style.ResetToDefaults();
            }
        }

        partial void OnIsXLogChanged(bool value) => _repaint?.Invoke();
        partial void OnIsYLogChanged(bool value) => _repaint?.Invoke();
        partial void OnShowMajorGridChanged(bool value) => _repaint?.Invoke();
        partial void OnShowMinorGridChanged(bool value) => _repaint?.Invoke();
        partial void OnUseCustomXLimitsChanged(bool value) => _repaint?.Invoke();
        partial void OnXMinChanged(double value) => _repaint?.Invoke();
        partial void OnXMaxChanged(double value) => _repaint?.Invoke();
        partial void OnUseCustomYLimitsChanged(bool value) => _repaint?.Invoke();
        partial void OnYMinChanged(double value) => _repaint?.Invoke();
        partial void OnYMaxChanged(double value) => _repaint?.Invoke();
    }
}