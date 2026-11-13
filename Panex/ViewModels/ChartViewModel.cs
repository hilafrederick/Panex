using CommunityToolkit.Mvvm.ComponentModel;
using Panex.Models;
using Panex.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Panex.ViewModels;

public class HoverCurveCache
{
    public string Label { get; set; } = string.Empty;
    public double[] Xs { get; set; } = Array.Empty<double>();
    public double[] Ys { get; set; } = Array.Empty<double>();
    public Windows.UI.Color UiColor { get; set; }
}

public partial class ChartViewModel : ObservableObject
{
    public event EventHandler? RequestRepaint;
    private readonly BuildupCalculator _buildupCalc;
    private readonly PeriodicTableService _ptService;
    private Dictionary<string, CurveStyle> _globalCurveStyles = new();

    private readonly Windows.UI.Color[] _compareColors = new[]
    {
        Microsoft.UI.Colors.Red, Microsoft.UI.Colors.Blue, Microsoft.UI.Colors.Green,
        Microsoft.UI.Colors.DarkOrange, Microsoft.UI.Colors.Purple, Microsoft.UI.Colors.Teal,
        Microsoft.UI.Colors.Magenta, Microsoft.UI.Colors.Brown, Microsoft.UI.Colors.Navy
    };

    public PlotSettings CrossSectionSettings { get; }
    public PlotSettings MacSettings { get; }
    public PlotSettings LacSettings { get; }
    public PlotSettings DepthsSettings { get; }
    public PlotSettings ZeffSettings { get; }
    public PlotSettings BuildupSettings { get; }
    public PlotSettings ZeqSettings { get; }

    [ObservableProperty] private bool isDarkMode = false;
    public Dictionary<PlotSettings, List<HoverCurveCache>> HoverDataMap { get; } = new();

    public ChartViewModel(PeriodicTableService ptService, BuildupCalculator buildupCalc)
    {
        _ptService = ptService;
        _buildupCalc = buildupCalc;
        Action repaint = () => RequestRepaint?.Invoke(this, EventArgs.Empty);

        CrossSectionSettings = new PlotSettings(true, true, repaint) { ShowMinorGrid = false };
        MacSettings = new PlotSettings(true, true, repaint) { ShowMinorGrid = false };
        LacSettings = new PlotSettings(true, true, repaint) { ShowMinorGrid = false };
        DepthsSettings = new PlotSettings(true, false, repaint) { ShowMinorGrid = true };
        ZeffSettings = new PlotSettings(true, false, repaint) { ShowMinorGrid = true };
        BuildupSettings = new PlotSettings(true, true, repaint) { ShowMinorGrid = true };
        ZeqSettings = new PlotSettings(true, false, repaint) { ShowMinorGrid = true };

        CrossSectionSettings.CaptureDefaults();
        MacSettings.CaptureDefaults();
        LacSettings.CaptureDefaults();
        DepthsSettings.CaptureDefaults();
        ZeffSettings.CaptureDefaults();
        BuildupSettings.CaptureDefaults();
        ZeqSettings.CaptureDefaults();
    }

    private void AddCurve(ScottPlot.Plot plot, PlotSettings settings, string label, double[] xs, double[] ys, ScottPlot.Color color, ScottPlot.LinePattern pattern, double lineWidth)
    {
        if (plot == null || xs == null || ys == null || xs.Length == 0 || xs.Length != ys.Length) return;

        int validCount = 0;
        for (int i = 0; i < xs.Length; i++)
        {
            if ((!settings.IsXLog || xs[i] > 0) && (!settings.IsYLog || ys[i] > 0)) validCount++;
        }
        if (validCount == 0) return;

        double[] plotXs = new double[validCount];
        double[] plotYs = new double[validCount];
        int idx = 0;

        for (int i = 0; i < xs.Length; i++)
        {
            if ((!settings.IsXLog || xs[i] > 0) && (!settings.IsYLog || ys[i] > 0))
            {
                plotXs[idx] = settings.IsXLog ? Math.Log10(xs[i]) : xs[i];
                plotYs[idx] = settings.IsYLog ? Math.Log10(ys[i]) : ys[i];
                idx++;
            }
        }

        var sp = plot.Add.Scatter(plotXs, plotYs);
        sp.LegendText = label;
        sp.Color = color;
        sp.LineWidth = (float)lineWidth;
        sp.LinePattern = pattern;
        sp.MarkerSize = 0;

        if (!HoverDataMap.ContainsKey(settings)) HoverDataMap[settings] = new List<HoverCurveCache>();
        HoverDataMap[settings].Add(new HoverCurveCache { Label = label, Xs = plotXs, Ys = plotYs, UiColor = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B) });
    }

    private void ConfigureAxes(ScottPlot.Plot plot, PlotSettings settings, string yLabel, ScottPlot.Alignment legendAlignment, double? minXRaw = null, double? maxXRaw = null, double? minYRaw = null)
    {
        plot.Axes.Bottom.Label.Text = "Energy (MeV)";
        plot.Axes.Left.Label.Text = yLabel;
        plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
        plot.DataBackground.Color = IsDarkMode ? ScottPlot.Color.FromHex("#1C1C1C") : ScottPlot.Colors.White;
        plot.Axes.Color(IsDarkMode ? ScottPlot.Colors.LightGray : ScottPlot.Colors.Black);

        plot.Grid.MajorLineColor = settings.ShowMajorGrid ? (IsDarkMode ? ScottPlot.Colors.White.WithOpacity(0.1) : ScottPlot.Colors.Black.WithOpacity(0.15)) : ScottPlot.Colors.Transparent;
        plot.Grid.MajorLineWidth = settings.ShowMajorGrid ? 1f : 0f;
        plot.Grid.MinorLineColor = settings.ShowMinorGrid ? (IsDarkMode ? ScottPlot.Colors.White.WithOpacity(0.05) : ScottPlot.Colors.Black.WithOpacity(0.05)) : ScottPlot.Colors.Transparent;
        plot.Grid.MinorLineWidth = settings.ShowMinorGrid ? 1f : 0f;

        if (settings.IsXLog)
        {
            var logTickGenX = new ScottPlot.TickGenerators.NumericManual();
            for (int i = -3; i <= 5; i++)
            {
                logTickGenX.AddMajor(i, GetSuperscript10(i));
                for (int j = 2; j <= 9; j++) logTickGenX.AddMinor(i + Math.Log10(j));
            }
            plot.Axes.Bottom.TickGenerator = logTickGenX;
        }
        else plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();

        if (settings.IsYLog)
        {
            var logTickGenY = new ScottPlot.TickGenerators.NumericManual();
            for (int i = -20; i <= 20; i++)
            {
                if (i % 3 == 0) logTickGenY.AddMajor(i, GetSuperscript10(i));
                else logTickGenY.AddMinor(i);
                for (int j = 2; j <= 9; j++) logTickGenY.AddMinor(i + Math.Log10(j));
            }
            plot.Axes.Left.TickGenerator = logTickGenY;
        }
        else plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();

        plot.Axes.AutoScale();

        if (settings.UseCustomXLimits || settings.UseCustomYLimits)
        {
            var limits = plot.Axes.GetLimits();
            double finalXMin = settings.UseCustomXLimits ? (settings.IsXLog && settings.XMin > 0 ? Math.Log10(settings.XMin) : settings.XMin) : limits.Left;
            double finalXMax = settings.UseCustomXLimits ? (settings.IsXLog && settings.XMax > 0 ? Math.Log10(settings.XMax) : settings.XMax) : limits.Right;

            if (!settings.UseCustomXLimits)
            {
                double rawXMin = minXRaw ?? 0.001;
                double rawXMax = maxXRaw ?? 100_000.0;
                finalXMin = settings.IsXLog ? Math.Log10(rawXMin) : rawXMin;
                finalXMax = settings.IsXLog ? Math.Log10(rawXMax) : rawXMax;
            }

            double finalYMin = settings.UseCustomYLimits ? (settings.IsYLog && settings.YMin > 0 ? Math.Log10(settings.YMin) : settings.YMin) : limits.Bottom;
            double finalYMax = settings.UseCustomYLimits ? (settings.IsYLog && settings.YMax > 0 ? Math.Log10(settings.YMax) : settings.YMax) : limits.Top;

            if (!settings.UseCustomYLimits && minYRaw.HasValue)
            {
                finalYMin = settings.IsYLog ? Math.Log10(minYRaw.Value) : minYRaw.Value;
                finalYMax = limits.Top > finalYMin ? limits.Top * 1.05 : finalYMin + (settings.IsYLog ? 1 : 10);
            }

            plot.Axes.SetLimits(finalXMin, finalXMax, finalYMin, finalYMax);
        }
        else
        {
            double rawXMin = minXRaw ?? 0.001;
            double rawXMax = maxXRaw ?? 100_000.0;
            plot.Axes.SetLimitsX(settings.IsXLog ? Math.Log10(rawXMin) : rawXMin, settings.IsXLog ? Math.Log10(rawXMax) : rawXMax);

            if (minYRaw.HasValue)
            {
                var limits = plot.Axes.GetLimits();
                double scaledMinY = settings.IsYLog ? Math.Log10(minYRaw.Value) : minYRaw.Value;
                double topPadding = limits.Top > scaledMinY ? limits.Top * 1.05 : scaledMinY + (settings.IsYLog ? 1 : 10);
                plot.Axes.SetLimitsY(scaledMinY, topPadding);
            }
        }

        plot.ShowLegend(legendAlignment);
        plot.Legend.BackgroundFill.Color = IsDarkMode ? ScottPlot.Color.FromHex("#2B2B2B") : ScottPlot.Colors.White;
        plot.Legend.FontColor = IsDarkMode ? ScottPlot.Colors.LightGray : ScottPlot.Colors.Black;
        plot.Legend.OutlineStyle.Color = IsDarkMode ? ScottPlot.Colors.Transparent : ScottPlot.Color.FromHex("#E5E5E5");
    }

    private string GetSuperscript10(int power)
    {
        string powerStr = power.ToString();
        string superscript = "";
        foreach (char c in powerStr)
        {
            superscript += c switch
            {
                '-' => '⁻',
                '0' => '⁰',
                '1' => '¹',
                '2' => '²',
                '3' => '³',
                '4' => '⁴',
                '5' => '⁵',
                '6' => '⁶',
                '7' => '⁷',
                '8' => '⁸',
                '9' => '⁹',
                _ => c
            };
        }
        return "10" + superscript;
    }

    private CurveStyle GetOrCreateCurveStyle(string name, Windows.UI.Color defaultColor, ScottPlot.LinePattern defaultPattern)
    {
        if (!_globalCurveStyles.TryGetValue(name, out var style))
        {
            Action repaint = () => RequestRepaint?.Invoke(this, EventArgs.Empty);
            style = new CurveStyle(name, defaultColor, defaultPattern, repaint) { LineWidth = 2 };
            _globalCurveStyles[name] = style;
        }
        return style;
    }

    private void SyncCurveStyles(PlotSettings settings, List<CurveStyle> activeStyles)
    {
        for (int i = settings.CurveStyles.Count - 1; i >= 0; i--)
            if (!activeStyles.Contains(settings.CurveStyles[i])) settings.CurveStyles.RemoveAt(i);

        foreach (var style in activeStyles)
            if (!settings.CurveStyles.Contains(style)) settings.CurveStyles.Add(style);
    }


    // =========================================================================================
    // SINGLE MATERIAL PLOTTING METHODS
    // =========================================================================================

    public void PlotCrossSectionTo(ScottPlot.Plot plot, Material? mat, bool s1, bool s2, bool s3, bool s4, bool s5, bool s6, bool s7, ObservableCollection<SelectableItem> iso, ObservableCollection<SelectableItem> sub)
        => PlotStandardPhysicsData(plot, CrossSectionSettings, "Cross Section (barns/atom)", 1.0, mat, s1, s2, s3, s4, s5, s6, s7, iso, sub);

    public void PlotMacTo(ScottPlot.Plot plot, Material? mat, bool s1, bool s2, bool s3, bool s4, bool s5, bool s6, bool s7, ObservableCollection<SelectableItem> iso, ObservableCollection<SelectableItem> sub)
        => PlotStandardPhysicsData(plot, MacSettings, "Mass Attenuation Coeff. (cm²/g)", mat?.MassAttenuationFactor ?? 1.0, mat, s1, s2, s3, s4, s5, s6, s7, iso, sub);

    public void PlotLacTo(ScottPlot.Plot plot, Material? mat, bool s1, bool s2, bool s3, bool s4, bool s5, bool s6, bool s7, ObservableCollection<SelectableItem> iso, ObservableCollection<SelectableItem> sub)
        => PlotStandardPhysicsData(plot, LacSettings, "Linear Attenuation Coeff. (cm⁻¹)", (mat?.MassAttenuationFactor ?? 1.0) * (mat?.Density ?? 0.0), mat, s1, s2, s3, s4, s5, s6, s7, iso, sub);

    private void PlotStandardPhysicsData(ScottPlot.Plot plot, PlotSettings settings, string yAxisTitle, double multiplier, Material? material, bool showTotal, bool showCoherent, bool showIncoherent, bool showPhotoelectric, bool showPair, bool showTriplet, bool showPhotonuclear, ObservableCollection<SelectableItem> isotopes, ObservableCollection<SelectableItem> subshells)
    {
        var activeStyles = new List<CurveStyle>();
        if (plot == null) return;

        plot.Clear();
        HoverDataMap[settings] = new List<HoverCurveCache>();

        if (material == null)
        {
            ConfigureAxes(plot, settings, yAxisTitle, ScottPlot.Alignment.LowerLeft);
            SyncCurveStyles(settings, activeStyles);
            return;
        }

        double[] Scale(double[] arr)
        {
            if (multiplier == 1.0) return arr;
            var res = new double[arr.Length];
            for (int i = 0; i < arr.Length; i++) res[i] = arr[i] * multiplier;
            return res;
        }

        if (showTotal)
        {
            var c = material.GetTotalCrossSectionCurve();
            var style = GetOrCreateCurveStyle("Total", Microsoft.UI.Colors.Black, ScottPlot.LinePattern.Solid);
            activeStyles.Add(style);
            AddCurve(plot, settings, "Total", c.Energies, Scale(c.CrossSections), style.SpColor, style.SelectedPattern, style.LineWidth);
        }
        if (showPhotoelectric)
        {
            var c = material.GetCrossSectionCurve(73, 0);
            var style = GetOrCreateCurveStyle("Photoelectric Absorption", Microsoft.UI.Colors.Red, ScottPlot.LinePattern.Solid);
            activeStyles.Add(style);
            AddCurve(plot, settings, "Photoelectric Absorption", c.Energies, Scale(c.CrossSections), style.SpColor, style.SelectedPattern, style.LineWidth);
        }
        if (showCoherent)
        {
            var c = material.GetCrossSectionCurve(71, 0);
            var style = GetOrCreateCurveStyle("Coherent Scattering", Microsoft.UI.Colors.Green, ScottPlot.LinePattern.Solid);
            activeStyles.Add(style);
            AddCurve(plot, settings, "Coherent Scattering", c.Energies, Scale(c.CrossSections), style.SpColor, style.SelectedPattern, style.LineWidth);
        }
        if (showIncoherent)
        {
            var c = material.GetCrossSectionCurve(72, 0);
            var style = GetOrCreateCurveStyle("Incoherent Scattering", Microsoft.UI.Colors.Blue, ScottPlot.LinePattern.Solid);
            activeStyles.Add(style);
            AddCurve(plot, settings, "Incoherent Scattering", c.Energies, Scale(c.CrossSections), style.SpColor, style.SelectedPattern, style.LineWidth);
        }
        if (showPair)
        {
            var c = material.GetCrossSectionCurve(74, 0);
            var style = GetOrCreateCurveStyle("Pair Production (Nuclear)", Microsoft.UI.Colors.DarkOrange, ScottPlot.LinePattern.Dashed);
            activeStyles.Add(style);
            AddCurve(plot, settings, "Pair Production (Nuclear)", c.Energies, Scale(c.CrossSections), style.SpColor, style.SelectedPattern, style.LineWidth);
        }
        if (showTriplet)
        {
            var c = material.GetCrossSectionCurve(75, 0);
            var style = GetOrCreateCurveStyle("Pair Production (Electron)", Microsoft.UI.Colors.Purple, ScottPlot.LinePattern.Dotted);
            activeStyles.Add(style);
            AddCurve(plot, settings, "Pair Production (Electron)", c.Energies, Scale(c.CrossSections), style.SpColor, style.SelectedPattern, style.LineWidth);
        }
        if (showPhotonuclear)
        {
            var c = material.GetPhotonuclearCrossSectionCurve();
            var style = GetOrCreateCurveStyle("Photonuclear Absorption", Microsoft.UI.Colors.Cyan, ScottPlot.LinePattern.Solid);
            activeStyles.Add(style);
            AddCurve(plot, settings, "Photonuclear Absorption", c.Energies, Scale(c.CrossSections), style.SpColor, style.SelectedPattern, style.LineWidth);
        }

        foreach (var item in isotopes.Where(i => i.IsSelected))
        {
            var el = material.Composition.Keys.FirstOrDefault(e => e.Z == item.Z);
            var iso = el?.Isotopes.FirstOrDefault(i => i.A == item.A);
            if (el != null && iso?.PhotonuclearData != null)
            {
                double scaleFactor = material.Composition[el] * iso.Abundance * multiplier;
                double[] scaledYs = new double[iso.PhotonuclearData.CrossSections.Length];
                for (int i = 0; i < scaledYs.Length; i++) scaledYs[i] = iso.PhotonuclearData.CrossSections[i] * scaleFactor;
                var style = GetOrCreateCurveStyle(item.Name, Microsoft.UI.Colors.DarkCyan, ScottPlot.LinePattern.Dashed);
                activeStyles.Add(style);
                AddCurve(plot, settings, item.Name, iso.PhotonuclearData.Energies, scaledYs, style.SpColor, style.SelectedPattern, style.LineWidth);
            }
        }

        foreach (var item in subshells.Where(s => s.IsSelected))
        {
            var el = material.Composition.Keys.FirstOrDefault(e => e.Z == item.Z);
            var curve = el?.GetPhotoatomicCurve(73, 91, item.SubshellIndex);

            if (el != null && curve != null && curve.Energies.Length > 0)
            {
                double scaleFactor = material.Composition[el] * multiplier;
                double[] scaledYs = new double[curve.CrossSections.Length];
                for (int i = 0; i < scaledYs.Length; i++) scaledYs[i] = curve.CrossSections[i] * scaleFactor;
                var style = GetOrCreateCurveStyle(item.Name, Microsoft.UI.Colors.PaleVioletRed, ScottPlot.LinePattern.Solid);
                activeStyles.Add(style);
                AddCurve(plot, settings, item.Name, curve.Energies, scaledYs, style.SpColor, style.SelectedPattern, style.LineWidth);

                var trueEdge = el.AbsorptionEdges?.FirstOrDefault(x => item.Name.Contains(x.Subshell));
                if (trueEdge != null)
                {
                    double plotX = settings.IsXLog ? Math.Log10(trueEdge.EnergyMeV) : trueEdge.EnergyMeV;
                    var vLine = plot.Add.VerticalLine(plotX);
                    vLine.Color = style.SpColor.WithOpacity(0.5f);
                    vLine.LinePattern = ScottPlot.LinePattern.Dotted;
                    vLine.LineWidth = 1.5f;
                }
            }
        }

        SyncCurveStyles(settings, activeStyles);
        ConfigureAxes(plot, settings, yAxisTitle, ScottPlot.Alignment.LowerLeft);
    }

    public void PlotDepthTo(ScottPlot.Plot plot, Material? material, double nValue, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
    {
        var activeStyles = new List<CurveStyle>();
        if (plot == null) return;

        plot.Clear();
        HoverDataMap[DepthsSettings] = new List<HoverCurveCache>();

        if (material == null)
        {
            ConfigureAxes(plot, DepthsSettings, "Penetration Depth (cm)", ScottPlot.Alignment.UpperLeft, minYRaw: 0);
            SyncCurveStyles(DepthsSettings, activeStyles);
            return;
        }

        double density = material.Density ?? 0.0;
        if (density > 0)
        {
            var energies = material.GetMasterEnergyGrid();
            double factor = material.MassAttenuationFactor * density;

            List<double> validEnergies = new();
            List<double> mfpList = new();
            List<double> nvlList = new();

            double logN = Math.Log(nValue);

            foreach (var e in energies)
            {
                double sumXs = 0;
                if (incCoh) sumXs += material.GetComponentCrossSection(e, 71);
                if (incInc) sumXs += material.GetComponentCrossSection(e, 72);
                if (incPe) sumXs += material.GetComponentCrossSection(e, 73);
                if (incPair) sumXs += material.GetComponentCrossSection(e, 74);
                if (incTrip) sumXs += material.GetComponentCrossSection(e, 75);
                if (incPn) sumXs += material.GetComponentCrossSection(e, 0);

                double lac = sumXs * factor;
                if (lac > 0)
                {
                    validEnergies.Add(e);
                    mfpList.Add(1.0 / lac);
                    nvlList.Add(logN / lac);
                }
            }

            if (validEnergies.Count > 0)
            {
                var styleMfp = GetOrCreateCurveStyle("Mean Free Path", Microsoft.UI.Colors.Black, ScottPlot.LinePattern.Dashed);
                activeStyles.Add(styleMfp);
                AddCurve(plot, DepthsSettings, "Mean Free Path", validEnergies.ToArray(), mfpList.ToArray(), styleMfp.SpColor, styleMfp.SelectedPattern, styleMfp.LineWidth);

                if (nValue > 1)
                {
                    string nvlLabel = nValue switch
                    {
                        2.0 => "HVL",
                        4.0 => "QVL",
                        10.0 => "TVL",
                        _ => $"1/{nValue:0.##} Value Layer"
                    };
                    var styleNvl = GetOrCreateCurveStyle(nvlLabel, Microsoft.UI.Colors.Blue, ScottPlot.LinePattern.Solid);
                    activeStyles.Add(styleNvl);
                    AddCurve(plot, DepthsSettings, nvlLabel, validEnergies.ToArray(), nvlList.ToArray(), styleNvl.SpColor, styleNvl.SelectedPattern, styleNvl.LineWidth);
                }
            }
        }

        SyncCurveStyles(DepthsSettings, activeStyles);
        ConfigureAxes(plot, DepthsSettings, "Penetration Depth (cm)", ScottPlot.Alignment.UpperLeft, minYRaw: 0);
    }

    public void PlotZeffTo(ScottPlot.Plot plot, Material? material)
    {
        var activeStyles = new List<CurveStyle>();
        if (plot == null) return;

        plot.Clear();
        HoverDataMap[ZeffSettings] = new List<HoverCurveCache>();

        if (material == null)
        {
            ConfigureAxes(plot, ZeffSettings, "Effective Atomic Number", ScottPlot.Alignment.LowerRight, minYRaw: 0);
            SyncCurveStyles(ZeffSettings, activeStyles);
            return;
        }

        var curve = material.GetEffectiveAtomicNumberCurve();
        var zeffObject = new CrossSectionCurve(0, 0, 0, curve.Energies, curve.ZeffValues);

        if (curve.Energies.Length > 0)
        {
            var styleDirect = GetOrCreateCurveStyle("Zeff (Direct)", Microsoft.UI.Colors.DarkMagenta, ScottPlot.LinePattern.Solid);
            activeStyles.Add(styleDirect);
            AddCurve(plot, ZeffSettings, "Zeff (Direct)", curve.Energies, curve.ZeffValues, styleDirect.SpColor, styleDirect.SelectedPattern, styleDirect.LineWidth);
        }

        var energies = material.GetMasterEnergyGrid();
        var totalC = material.GetTotalCrossSectionCurve();
        var totalXsObject = new CrossSectionCurve(0, 0, 0, totalC.Energies, totalC.CrossSections);

        List<double> interpXs = new();
        List<double> interpYs = new();

        int minZ = material.Composition.Keys.Min(e => e.Z);
        int maxZ = material.Composition.Keys.Max(e => e.Z);

        foreach (var e in energies.Where(e => e >= 0.001))
        {
            double st = totalXsObject.Interpolate(e);
            double directZeff = zeffObject.Interpolate(e);
            double interpZeff = _ptService.CalculateInterpolatedZeff(e, st, minZ, maxZ, directZeff);
            if (interpZeff > 0)
            {
                interpXs.Add(e);
                interpYs.Add(interpZeff);
            }
        }

        if (interpXs.Count > 0)
        {
            var styleInterp = GetOrCreateCurveStyle("Zeff (Interpolated)", Microsoft.UI.Colors.DarkOrange, ScottPlot.LinePattern.Dashed);
            activeStyles.Add(styleInterp);
            AddCurve(plot, ZeffSettings, "Zeff (Interpolated)", interpXs.ToArray(), interpYs.ToArray(), styleInterp.SpColor, styleInterp.SelectedPattern, styleInterp.LineWidth);
        }

        SyncCurveStyles(ZeffSettings, activeStyles);
        ConfigureAxes(plot, ZeffSettings, "Effective Atomic Number", ScottPlot.Alignment.LowerRight, minYRaw: 0);
    }

    public void PlotZeqTo(ScottPlot.Plot plot, Material? material)
    {
        var activeStyles = new List<CurveStyle>();
        if (plot == null) return;

        plot.Clear();
        HoverDataMap[ZeqSettings] = new List<HoverCurveCache>();

        if (material == null)
        {
            ConfigureAxes(plot, ZeqSettings, "Equivalent Atomic Number (Zeq)", ScottPlot.Alignment.LowerRight, 0.01, 20.0, minYRaw: 0);
            SyncCurveStyles(ZeqSettings, activeStyles);
            return;
        }

        List<double> xs = new(), ys = new();
        foreach (var e in BuildupCalculator.AnsiEnergiesMeV)
        {
            double zeq = _buildupCalc.GetEquivalentAtomicNumber(material, e);
            if (zeq > 0) { xs.Add(e); ys.Add(zeq); }
        }

        if (xs.Count > 0)
        {
            var styleZeq = GetOrCreateCurveStyle("Equivalent Atomic Number (Zeq)", Microsoft.UI.Colors.Teal, ScottPlot.LinePattern.Solid);
            activeStyles.Add(styleZeq);
            AddCurve(plot, ZeqSettings, "Equivalent Atomic Number (Zeq)", xs.ToArray(), ys.ToArray(), styleZeq.SpColor, styleZeq.SelectedPattern, styleZeq.LineWidth);
        }

        SyncCurveStyles(ZeqSettings, activeStyles);
        ConfigureAxes(plot, ZeqSettings, "Equivalent Atomic Number (Zeq)", ScottPlot.Alignment.LowerRight, 0.01, 20.0, minYRaw: 0);
    }

    public void PlotBuildupTo(ScottPlot.Plot plot, Material? material, double[] targetMfps, BuildupFactorType type)
    {
        var activeStyles = new List<CurveStyle>();
        if (plot == null) return;

        plot.Clear();
        HoverDataMap[BuildupSettings] = new List<HoverCurveCache>();

        if (material == null)
        {
            ConfigureAxes(plot, BuildupSettings, $"Buildup Factor ({type})", ScottPlot.Alignment.UpperLeft, 0.01, 20.0, minYRaw: 0);
            SyncCurveStyles(BuildupSettings, activeStyles);
            return;
        }

        Windows.UI.Color[] uiColors = { Microsoft.UI.Colors.Red, Microsoft.UI.Colors.DarkOrange, Microsoft.UI.Colors.Green, Microsoft.UI.Colors.Blue, Microsoft.UI.Colors.Purple, Microsoft.UI.Colors.Magenta, Microsoft.UI.Colors.Brown };

        for (int i = 0; i < targetMfps.Length; i++)
        {
            double mfp = targetMfps[i];
            List<double> xs = new(), ys = new();

            foreach (var e in BuildupCalculator.AnsiEnergiesMeV)
            {
                double zeq = _buildupCalc.GetEquivalentAtomicNumber(material, e);
                var gp = _buildupCalc.GetGpParameters(material, e, type);

                if (gp != null && zeq > 0 && gp.A != 0)
                {
                    double bf = _buildupCalc.CalculateBuildupFactor(material, e, mfp, type);
                    if (bf >= 1.0) { xs.Add(e); ys.Add(bf); }
                }
            }

            if (xs.Count > 0)
            {
                string label = $"{targetMfps[i]} MFP";
                var style = GetOrCreateCurveStyle(label, uiColors[i % uiColors.Length], ScottPlot.LinePattern.Solid);
                activeStyles.Add(style);
                AddCurve(plot, BuildupSettings, label, xs.ToArray(), ys.ToArray(), style.SpColor, style.SelectedPattern, style.LineWidth);
            }
        }

        SyncCurveStyles(BuildupSettings, activeStyles);
        ConfigureAxes(plot, BuildupSettings, $"Buildup Factor ({type})", ScottPlot.Alignment.UpperLeft, 0.01, 20.0, minYRaw: 0);
    }


    // =========================================================================================
    // MULTIPLE MATERIAL COMPARISON PLOTTING METHODS
    // =========================================================================================

    private void PlotComparisonPhysicsData(ScottPlot.Plot plot, PlotSettings settings, string yAxisTitle, IEnumerable<Material> materials, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn, Func<Material, double> multiplierSelector)
    {
        if (plot == null) return;
        plot.Clear();
        HoverDataMap[settings] = new List<HoverCurveCache>();
        var activeStyles = new List<CurveStyle>();

        if (materials == null || !materials.Any())
        {
            ConfigureAxes(plot, settings, yAxisTitle, ScottPlot.Alignment.LowerLeft);
            SyncCurveStyles(settings, activeStyles);
            return;
        }

        int colorIdx = 0;
        foreach (var mat in materials)
        {
            double multiplier = multiplierSelector(mat);
            // Skip linear attenuation for materials without density
            if (multiplier == 0 && settings == LacSettings) continue;

            var curve = mat.GetCustomTotalCurve(incPe, incCoh, incInc, incPair, incTrip, incPn);

            double[] scaledYs = new double[curve.Values.Length];
            for (int i = 0; i < scaledYs.Length; i++) scaledYs[i] = curve.Values[i] * multiplier;

            var style = GetOrCreateCurveStyle(mat.Name, _compareColors[colorIdx % _compareColors.Length], ScottPlot.LinePattern.Solid);
            activeStyles.Add(style);

            AddCurve(plot, settings, mat.Name, curve.Energies, scaledYs, style.SpColor, style.SelectedPattern, style.LineWidth);
            colorIdx++;
        }

        SyncCurveStyles(settings, activeStyles);
        ConfigureAxes(plot, settings, yAxisTitle, ScottPlot.Alignment.LowerLeft);
    }

    public void PlotCrossSectionComparisonTo(ScottPlot.Plot plot, IEnumerable<Material> materials, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
        => PlotComparisonPhysicsData(plot, CrossSectionSettings, "Cross Section (barns/atom)", materials, incPe, incCoh, incInc, incPair, incTrip, incPn, m => 1.0);

    public void PlotMacComparisonTo(ScottPlot.Plot plot, IEnumerable<Material> materials, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
        => PlotComparisonPhysicsData(plot, MacSettings, "Mass Attenuation Coeff. (cm²/g)", materials, incPe, incCoh, incInc, incPair, incTrip, incPn, m => m.MassAttenuationFactor);

    public void PlotLacComparisonTo(ScottPlot.Plot plot, IEnumerable<Material> materials, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
        => PlotComparisonPhysicsData(plot, LacSettings, "Linear Attenuation Coeff. (cm⁻¹)", materials, incPe, incCoh, incInc, incPair, incTrip, incPn, m => m.MassAttenuationFactor * (m.Density ?? 0.0));

    public void PlotDepthComparisonTo(ScottPlot.Plot plot, IEnumerable<Material> materials, double nValue, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
    {
        if (plot == null) return;
        plot.Clear();
        HoverDataMap[DepthsSettings] = new List<HoverCurveCache>();
        var activeStyles = new List<CurveStyle>();

        if (materials == null || !materials.Any())
        {
            ConfigureAxes(plot, DepthsSettings, "Penetration Depth (cm)", ScottPlot.Alignment.UpperLeft, minYRaw: 0);
            SyncCurveStyles(DepthsSettings, activeStyles);
            return;
        }

        double logN = Math.Log(nValue);
        string yLabel = nValue == 1.0 ? "Mean Free Path (cm)" : (nValue == 2.0 ? "HVL (cm)" : (nValue == 10.0 ? "TVL (cm)" : $"1/{nValue:0.##} VL (cm)"));

        int colorIdx = 0;
        foreach (var mat in materials)
        {
            double density = mat.Density ?? 0.0;
            if (density <= 0) continue;

            double factor = mat.MassAttenuationFactor * density;
            var curve = mat.GetCustomTotalCurve(incPe, incCoh, incInc, incPair, incTrip, incPn);

            List<double> validXs = new();
            List<double> validYs = new();

            for (int i = 0; i < curve.Energies.Length; i++)
            {
                double lac = curve.Values[i] * factor;
                if (lac > 0)
                {
                    validXs.Add(curve.Energies[i]);
                    validYs.Add(nValue <= 1.0 ? 1.0 / lac : logN / lac);
                }
            }

            if (validXs.Count > 0)
            {
                var style = GetOrCreateCurveStyle(mat.Name, _compareColors[colorIdx % _compareColors.Length], ScottPlot.LinePattern.Solid);
                activeStyles.Add(style);
                AddCurve(plot, DepthsSettings, mat.Name, validXs.ToArray(), validYs.ToArray(), style.SpColor, style.SelectedPattern, style.LineWidth);
                colorIdx++;
            }
        }

        SyncCurveStyles(DepthsSettings, activeStyles);
        ConfigureAxes(plot, DepthsSettings, yLabel, ScottPlot.Alignment.UpperLeft, minYRaw: 0);
    }

    public void PlotZeffComparisonTo(ScottPlot.Plot plot, IEnumerable<Material> materials, bool useInterpolated, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
    {
        if (plot == null) return;
        plot.Clear();
        HoverDataMap[ZeffSettings] = new List<HoverCurveCache>();
        var activeStyles = new List<CurveStyle>();

        if (materials == null || !materials.Any())
        {
            ConfigureAxes(plot, ZeffSettings, "Effective Atomic Number", ScottPlot.Alignment.LowerRight, minYRaw: 0);
            SyncCurveStyles(ZeffSettings, activeStyles);
            return;
        }

        int colorIdx = 0;
        foreach (var mat in materials)
        {
            if (!useInterpolated)
            {
                // Direct Zeff respects the selected interaction filters
                var curve = mat.GetCustomZeffCurve(incPe, incCoh, incInc, incPair, incTrip, incPn);

                if (curve.Energies.Length > 0)
                {
                    var style = GetOrCreateCurveStyle(mat.Name + " (Direct)", _compareColors[colorIdx % _compareColors.Length], ScottPlot.LinePattern.Solid);
                    activeStyles.Add(style);
                    AddCurve(plot, ZeffSettings, mat.Name, curve.Energies, curve.ZeffValues, style.SpColor, style.SelectedPattern, style.LineWidth);
                    colorIdx++;
                }
            }
            else
            {
                // Interpolated Zeff compares total cross section against the elemental baseline
                var energies = mat.GetMasterEnergyGrid();
                var totalC = mat.GetTotalCrossSectionCurve();
                var zeffCurve = mat.GetEffectiveAtomicNumberCurve();

                var totalXsObject = new CrossSectionCurve(0, 0, 0, totalC.Energies, totalC.CrossSections);
                var zeffObject = new CrossSectionCurve(0, 0, 0, zeffCurve.Energies, zeffCurve.ZeffValues);

                List<double> interpXs = new();
                List<double> interpYs = new();

                int minZ = mat.Composition.Keys.Min(e => e.Z);
                int maxZ = mat.Composition.Keys.Max(e => e.Z);

                foreach (var e in energies.Where(e => e >= 0.001))
                {
                    double st = totalXsObject.Interpolate(e);
                    double directZeff = zeffObject.Interpolate(e);
                    double interpZeff = _ptService.CalculateInterpolatedZeff(e, st, minZ, maxZ, directZeff);
                    if (interpZeff > 0)
                    {
                        interpXs.Add(e);
                        interpYs.Add(interpZeff);
                    }
                }

                if (interpXs.Count > 0)
                {
                    var style = GetOrCreateCurveStyle(mat.Name + " (Interpolated)", _compareColors[colorIdx % _compareColors.Length], ScottPlot.LinePattern.Dashed);
                    activeStyles.Add(style);
                    AddCurve(plot, ZeffSettings, mat.Name, interpXs.ToArray(), interpYs.ToArray(), style.SpColor, style.SelectedPattern, style.LineWidth);
                    colorIdx++;
                }
            }
        }

        SyncCurveStyles(ZeffSettings, activeStyles);
        ConfigureAxes(plot, ZeffSettings, "Effective Atomic Number", ScottPlot.Alignment.LowerRight, minYRaw: 0);
    }

    public void PlotZeqComparisonTo(ScottPlot.Plot plot, IEnumerable<Material> materials)
    {
        if (plot == null) return;
        plot.Clear();
        HoverDataMap[ZeqSettings] = new List<HoverCurveCache>();
        var activeStyles = new List<CurveStyle>();

        if (materials == null || !materials.Any())
        {
            ConfigureAxes(plot, ZeqSettings, "Equivalent Atomic Number (Zeq)", ScottPlot.Alignment.LowerRight, 0.01, 20.0, minYRaw: 0);
            SyncCurveStyles(ZeqSettings, activeStyles);
            return;
        }

        int colorIdx = 0;
        foreach (var mat in materials)
        {
            List<double> xs = new(), ys = new();
            foreach (var e in BuildupCalculator.AnsiEnergiesMeV)
            {
                double zeq = _buildupCalc.GetEquivalentAtomicNumber(mat, e);
                if (zeq > 0) { xs.Add(e); ys.Add(zeq); }
            }

            if (xs.Count > 0)
            {
                var style = GetOrCreateCurveStyle(mat.Name, _compareColors[colorIdx % _compareColors.Length], ScottPlot.LinePattern.Solid);
                activeStyles.Add(style);
                AddCurve(plot, ZeqSettings, mat.Name, xs.ToArray(), ys.ToArray(), style.SpColor, style.SelectedPattern, style.LineWidth);
                colorIdx++;
            }
        }

        SyncCurveStyles(ZeqSettings, activeStyles);
        ConfigureAxes(plot, ZeqSettings, "Equivalent Atomic Number (Zeq)", ScottPlot.Alignment.LowerRight, 0.01, 20.0, minYRaw: 0);
    }

    public void PlotBuildupComparisonTo(ScottPlot.Plot plot, IEnumerable<Material> materials, double targetMfp, BuildupFactorType type)
    {
        if (plot == null) return;
        plot.Clear();
        HoverDataMap[BuildupSettings] = new List<HoverCurveCache>();
        var activeStyles = new List<CurveStyle>();

        if (materials == null || !materials.Any())
        {
            ConfigureAxes(plot, BuildupSettings, $"Buildup Factor ({type}) at {targetMfp} MFP", ScottPlot.Alignment.UpperLeft, 0.01, 20.0, minYRaw: 0);
            SyncCurveStyles(BuildupSettings, activeStyles);
            return;
        }

        int colorIdx = 0;
        foreach (var mat in materials)
        {
            List<double> xs = new(), ys = new();
            foreach (var e in BuildupCalculator.AnsiEnergiesMeV)
            {
                double zeq = _buildupCalc.GetEquivalentAtomicNumber(mat, e);
                var gp = _buildupCalc.GetGpParameters(mat, e, type);

                if (gp != null && zeq > 0 && gp.A != 0)
                {
                    double bf = _buildupCalc.CalculateBuildupFactor(mat, e, targetMfp, type);
                    if (bf >= 1.0) { xs.Add(e); ys.Add(bf); }
                }
            }

            if (xs.Count > 0)
            {
                var style = GetOrCreateCurveStyle(mat.Name, _compareColors[colorIdx % _compareColors.Length], ScottPlot.LinePattern.Solid);
                activeStyles.Add(style);
                AddCurve(plot, BuildupSettings, mat.Name, xs.ToArray(), ys.ToArray(), style.SpColor, style.SelectedPattern, style.LineWidth);
                colorIdx++;
            }
        }

        SyncCurveStyles(BuildupSettings, activeStyles);
        ConfigureAxes(plot, BuildupSettings, $"Buildup Factor ({type}) at {targetMfp} MFP", ScottPlot.Alignment.UpperLeft, 0.01, 20.0, minYRaw: 0);
    }
}