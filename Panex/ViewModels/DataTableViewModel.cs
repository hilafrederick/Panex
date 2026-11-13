using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Panex.Models;
using Panex.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace Panex.ViewModels;

public partial class DataTableViewModel : ObservableObject
{
    private readonly PeriodicTableService _ptService;
    private readonly BuildupCalculator _buildupCalc;

    [ObservableProperty] private ObservableCollection<PhysicsTableRow>? crossSectionData;
    [ObservableProperty] private ObservableCollection<ColumnHeader>? crossSectionHeaders;

    [ObservableProperty] private ObservableCollection<PhysicsTableRow>? macData;
    [ObservableProperty] private ObservableCollection<ColumnHeader>? macHeaders;

    [ObservableProperty] private ObservableCollection<PhysicsTableRow>? lacData;
    [ObservableProperty] private ObservableCollection<ColumnHeader>? lacHeaders;

    [ObservableProperty] private ObservableCollection<PhysicsTableRow>? depthsData;
    [ObservableProperty] private ObservableCollection<ColumnHeader>? depthsHeaders;

    [ObservableProperty] private ObservableCollection<PhysicsTableRow>? zeffData;
    [ObservableProperty] private ObservableCollection<ColumnHeader>? zeffHeaders;

    [ObservableProperty] private ObservableCollection<PhysicsTableRow>? buildupData;
    [ObservableProperty] private ObservableCollection<ColumnHeader>? buildupHeaders;

    [ObservableProperty] private ObservableCollection<AbsorptionEdgeRow>? absorptionEdgesData;

    private Dictionary<Material, (ObservableCollection<PhysicsTableRow> Data, ObservableCollection<ColumnHeader> Headers)> _crossSectionCache = new();
    private Dictionary<Material, (ObservableCollection<PhysicsTableRow> Data, ObservableCollection<ColumnHeader> Headers)> _macCache = new();
    private Dictionary<Material, (ObservableCollection<PhysicsTableRow> Data, ObservableCollection<ColumnHeader> Headers)> _lacCache = new();
    private Dictionary<Material, (ObservableCollection<PhysicsTableRow> Data, ObservableCollection<ColumnHeader> Headers)> _depthsCache = new();
    private Dictionary<Material, (ObservableCollection<PhysicsTableRow> Data, ObservableCollection<ColumnHeader> Headers)> _zeffCache = new();
    private Dictionary<Material, (ObservableCollection<PhysicsTableRow> Data, ObservableCollection<ColumnHeader> Headers)> _buildupCache = new();

    public DataTableViewModel(PeriodicTableService ptService, BuildupCalculator buildupCalc)
    {
        _ptService = ptService;
        _buildupCalc = buildupCalc;
    }

    public void ClearCaches()
    {
        _crossSectionCache.Clear();
        _macCache.Clear();
        _lacCache.Clear();
        _depthsCache.Clear();
        _zeffCache.Clear();
        _buildupCache.Clear();
    }

    private void BuildTableGeneric(
        Material material,
        IEnumerable<double> energyGrid,
        ObservableCollection<ColumnHeader> headers,
        Func<double, IEnumerable<string>> rowGenerator,
        Dictionary<Material, (ObservableCollection<PhysicsTableRow> Data, ObservableCollection<ColumnHeader> Headers)> cache,
        Action<ObservableCollection<PhysicsTableRow>> setDataSet,
        Action<ObservableCollection<ColumnHeader>> setHeaders)
    {
        if (material == null) return;

        if (cache.TryGetValue(material, out var cached))
        {
            setHeaders(cached.Headers);
            setDataSet(cached.Data);
            return;
        }

        var dataList = new ObservableCollection<PhysicsTableRow>();
        string lastEnergyString = null;

        foreach (var e in energyGrid)
        {
            string currentEnergyString = e.ToString("G5");

            // Skip rows that format to the same G5 energy string
            if (currentEnergyString == lastEnergyString) continue;
            lastEnergyString = currentEnergyString;

            dataList.Add(new PhysicsTableRow
            {
                Energy = currentEnergyString,
                Values = rowGenerator(e).ToList()
            });
        }

        cache[material] = (dataList, headers);
        setHeaders(headers);
        setDataSet(dataList);
    }

    private void BuildComparisonTableGeneric(
        IEnumerable<Material> materials,
        IEnumerable<double> energyGrid,
        Func<Material, double, string> valueSelector,
        Action<ObservableCollection<PhysicsTableRow>> setDataSet,
        Action<ObservableCollection<ColumnHeader>> setHeaders,
        string unitHeader)
    {
        if (materials == null || !materials.Any())
        {
            setHeaders(new ObservableCollection<ColumnHeader>());
            setDataSet(new ObservableCollection<PhysicsTableRow>());
            return;
        }

        var headers = new ObservableCollection<ColumnHeader> { new ColumnHeader { Title = "Energy (MeV)" } };
        foreach (var mat in materials)
        {
            headers.Add(new ColumnHeader { Title = string.IsNullOrEmpty(unitHeader) ? mat.Name : $"{mat.Name} ({unitHeader})" });
        }

        var dataList = new ObservableCollection<PhysicsTableRow>();
        string lastEnergyString = null;

        foreach (var e in energyGrid)
        {
            string currentEnergyString = e.ToString("G5");

            // Skip rows that format to the same G5 energy string
            if (currentEnergyString == lastEnergyString) continue;
            lastEnergyString = currentEnergyString;

            var rowVals = new List<string> { currentEnergyString };
            foreach (var mat in materials)
            {
                rowVals.Add(valueSelector(mat, e));
            }
            dataList.Add(new PhysicsTableRow { Energy = currentEnergyString, Values = rowVals });
        }

        setHeaders(headers);
        setDataSet(dataList);
    }

    public void UpdateCrossSectionComparisonTable(IEnumerable<Material> materials, List<double> energies, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn) =>
        BuildComparisonTableGeneric(materials, energies, (mat, e) => mat.GetCustomTotalCrossSection(e, incPe, incCoh, incInc, incPair, incTrip, incPn).ToString("G6"), v => CrossSectionData = v, c => CrossSectionHeaders = c, "b");

    public void UpdateMacComparisonTable(IEnumerable<Material> materials, List<double> energies, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn) =>
        BuildComparisonTableGeneric(materials, energies, (mat, e) => (mat.GetCustomTotalCrossSection(e, incPe, incCoh, incInc, incPair, incTrip, incPn) * mat.MassAttenuationFactor).ToString("G6"), v => MacData = v, c => MacHeaders = c, "cm²/g");

    public void UpdateLacComparisonTable(IEnumerable<Material> materials, List<double> energies, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn) =>
        BuildComparisonTableGeneric(materials, energies, (mat, e) => (mat.Density == null || mat.Density <= 0) ? "N/A" : (mat.GetCustomTotalCrossSection(e, incPe, incCoh, incInc, incPair, incTrip, incPn) * mat.MassAttenuationFactor * mat.Density.Value).ToString("G6"), v => LacData = v, c => LacHeaders = c, "cm⁻¹");

    public void UpdateDepthsComparisonTable(IEnumerable<Material> materials, List<double> energies, double nValue, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
    {
        string unitHeader = nValue == 1.0 ? "MFP (cm)" : (nValue == 2.0 ? "HVL (cm)" : (nValue == 10.0 ? "TVL (cm)" : $"1/{nValue:0.##} VL (cm)"));
        double logN = Math.Log(nValue);

        BuildComparisonTableGeneric(materials, energies, (mat, e) =>
        {
            if (mat.Density == null || mat.Density <= 0) return "N/A";
            double lac = mat.GetCustomTotalCrossSection(e, incPe, incCoh, incInc, incPair, incTrip, incPn) * mat.MassAttenuationFactor * mat.Density.Value;
            if (lac <= 0) return "Infinity";
            return (nValue <= 1.0 ? 1.0 / lac : logN / lac).ToString("G6");
        }, v => DepthsData = v, c => DepthsHeaders = c, unitHeader);
    }

    public void UpdateZeffComparisonTable(IEnumerable<Material> materials, List<double> energies, bool useInterpolated, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
    {
        BuildComparisonTableGeneric(materials, energies, (mat, e) =>
        {
            if (!useInterpolated)
            {
                double sigmaAtomic = 0.0, sigmaElectronic = 0.0;
                foreach (var kvp in mat.Composition)
                {
                    var el = kvp.Key;
                    double fraction = kvp.Value;
                    double sig_i = 0.0;
                    if (incCoh) sig_i += el.GetPhotoatomicCurve(71, 0)?.Interpolate(e) ?? 0.0;
                    if (incInc) sig_i += el.GetPhotoatomicCurve(72, 0)?.Interpolate(e) ?? 0.0;
                    if (incPe) sig_i += el.GetPhotoatomicCurve(73, 0)?.Interpolate(e) ?? 0.0;
                    if (incPair) sig_i += el.GetPhotoatomicCurve(74, 0)?.Interpolate(e) ?? 0.0;
                    if (incTrip) sig_i += el.GetPhotoatomicCurve(75, 0)?.Interpolate(e) ?? 0.0;
                    if (incPn) sig_i += el.GetElementalPhotonuclearCurve().Interpolate(e);

                    sigmaAtomic += fraction * sig_i;
                    if (el.Z > 0) sigmaElectronic += fraction * (sig_i / el.Z);
                }
                return (sigmaElectronic > 0 ? sigmaAtomic / sigmaElectronic : 0.0).ToString("G6");
            }
            else
            {
                double st = mat.GetCrossSection(e);
                double directZeff = mat.GetEffectiveAtomicNumber(e);
                int minZ = mat.Composition.Keys.Min(el => el.Z);
                int maxZ = mat.Composition.Keys.Max(el => el.Z);
                double interpZeff = _ptService.CalculateInterpolatedZeff(e, st, minZ, maxZ, directZeff);
                return interpZeff > 0 ? interpZeff.ToString("G6") : "N/A";
            }
        }, v => ZeffData = v, c => ZeffHeaders = c, "");
    }

    public void UpdateBuildupComparisonTable(IEnumerable<Material> materials, double targetMfp, BuildupFactorType type)
    {
        BuildComparisonTableGeneric(materials, BuildupCalculator.AnsiEnergiesMeV, (mat, e) =>
        {
            double zeq = _buildupCalc.GetEquivalentAtomicNumber(mat, e);
            var gp = _buildupCalc.GetGpParameters(mat, e, type);
            if (gp != null && zeq > 0 && gp.A != 0)
            {
                double bf = _buildupCalc.CalculateBuildupFactor(mat, e, targetMfp, type);
                return bf >= 1.0 ? bf.ToString("G6") : "-";
            }
            return "-";
        }, v => BuildupData = v, c => BuildupHeaders = c, $"BF at {targetMfp} MFP");
    }

    private CrossSectionCurve? GetCurveAsObject((double[] Energies, double[] CrossSections) tuple) =>
        (tuple.Energies == null || tuple.Energies.Length == 0) ? null : new CrossSectionCurve(0, 0, 0, tuple.Energies, tuple.CrossSections);

    public void UpdateCrossSectionDataTable(Material material, List<double> energies, bool sTot, bool sCoh, bool sInc, bool sPe, bool sPairN, bool sPairE, bool sPn, ObservableCollection<SelectableItem> isos, ObservableCollection<SelectableItem> subs) =>
        UpdateStandardPhysicsTable("b", 1.0, material, energies, sTot, sCoh, sInc, sPe, sPairN, sPairE, sPn, isos, subs, _crossSectionCache, v => CrossSectionData = v, c => CrossSectionHeaders = c);

    public void UpdateMacDataTable(Material material, List<double> energies, bool sTot, bool sCoh, bool sInc, bool sPe, bool sPairN, bool sPairE, bool sPn, ObservableCollection<SelectableItem> isos, ObservableCollection<SelectableItem> subs) =>
        UpdateStandardPhysicsTable("cm²/g", material.MassAttenuationFactor, material, energies, sTot, sCoh, sInc, sPe, sPairN, sPairE, sPn, isos, subs, _macCache, v => MacData = v, c => MacHeaders = c);

    public void UpdateLacDataTable(Material material, List<double> energies, bool sTot, bool sCoh, bool sInc, bool sPe, bool sPairN, bool sPairE, bool sPn, ObservableCollection<SelectableItem> isos, ObservableCollection<SelectableItem> subs) =>
        UpdateStandardPhysicsTable("cm⁻¹", material.MassAttenuationFactor * (material.Density ?? 0.0), material, energies, sTot, sCoh, sInc, sPe, sPairN, sPairE, sPn, isos, subs, _lacCache, v => LacData = v, c => LacHeaders = c);

    private void UpdateStandardPhysicsTable(string unit, double mult, Material material, List<double> energies, bool sTot, bool sCoh, bool sInc, bool sPe, bool sPairN, bool sPairE, bool sPn, ObservableCollection<SelectableItem> isos, ObservableCollection<SelectableItem> subs, Dictionary<Material, (ObservableCollection<PhysicsTableRow>, ObservableCollection<ColumnHeader>)> cache, Action<ObservableCollection<PhysicsTableRow>> setDataSet, Action<ObservableCollection<ColumnHeader>> setHeaders)
    {
        var headers = new ObservableCollection<ColumnHeader> { new ColumnHeader { Title = "Energy (MeV)" } };
        if (sTot) headers.Add(new ColumnHeader { Title = $"Total ({unit})" });
        if (sCoh) headers.Add(new ColumnHeader { Title = $"Coherent ({unit})" });
        if (sInc) headers.Add(new ColumnHeader { Title = $"Incoherent ({unit})" });
        if (sPe) headers.Add(new ColumnHeader { Title = $"Photoelectric ({unit})" });
        if (sPairN) headers.Add(new ColumnHeader { Title = $"Pair (Nuclear) ({unit})" });
        if (sPairE) headers.Add(new ColumnHeader { Title = $"Pair (Electron) ({unit})" });
        if (sPn) headers.Add(new ColumnHeader { Title = $"Photonuclear ({unit})" });

        var activeIsotopes = isos.Where(i => i.IsSelected).ToList();
        var activeSubshells = subs.Where(s => s.IsSelected).ToList();

        foreach (var i in activeIsotopes) headers.Add(new ColumnHeader { Title = i.Name });
        foreach (var s in activeSubshells) headers.Add(new ColumnHeader { Title = s.Name });

        var totalC = sTot ? GetCurveAsObject(material.GetTotalCrossSectionCurve()) : null;
        var cohC = sCoh ? GetCurveAsObject(material.GetCrossSectionCurve(71, 0)) : null;
        var incohC = sInc ? GetCurveAsObject(material.GetCrossSectionCurve(72, 0)) : null;
        var peC = sPe ? GetCurveAsObject(material.GetCrossSectionCurve(73, 0)) : null;
        var pairC = sPairN ? GetCurveAsObject(material.GetCrossSectionCurve(74, 0)) : null;
        var tripC = sPairE ? GetCurveAsObject(material.GetCrossSectionCurve(75, 0)) : null;
        var pnC = sPn ? GetCurveAsObject(material.GetPhotonuclearCrossSectionCurve()) : null;

        BuildTableGeneric(material, energies, headers, (e) =>
        {
            var vals = new List<string> { e.ToString("G5") };
            if (sTot) vals.Add(((totalC?.Interpolate(e) ?? 0.0) * mult).ToString("G6"));
            if (sCoh) vals.Add(((cohC?.Interpolate(e) ?? 0.0) * mult).ToString("G6"));
            if (sInc) vals.Add(((incohC?.Interpolate(e) ?? 0.0) * mult).ToString("G6"));
            if (sPe) vals.Add(((peC?.Interpolate(e) ?? 0.0) * mult).ToString("G6"));
            if (sPairN) vals.Add(((pairC?.Interpolate(e) ?? 0.0) * mult).ToString("G6"));
            if (sPairE) vals.Add(((tripC?.Interpolate(e) ?? 0.0) * mult).ToString("G6"));
            if (sPn) vals.Add(((pnC?.Interpolate(e) ?? 0.0) * mult).ToString("G6"));

            foreach (var a in activeIsotopes)
            {
                var el = material.Composition.Keys.FirstOrDefault(x => x.Z == a.Z);
                var iso = el?.Isotopes.FirstOrDefault(x => x.A == a.A);
                vals.Add(((el != null && iso != null) ? iso.GetPhotonuclearCrossSection(e) * iso.Abundance * material.Composition[el] * mult : 0.0).ToString("G6"));
            }
            foreach (var s in activeSubshells)
            {
                var el = material.Composition.Keys.FirstOrDefault(x => x.Z == s.Z);
                var curve = el?.GetPhotoatomicCurve(73, 91, s.SubshellIndex);
                vals.Add(((el != null && curve != null) ? curve.Interpolate(e) * material.Composition[el] * mult : 0.0).ToString("G6"));
            }
            return vals;
        }, cache, setDataSet, setHeaders);
    }

    public void UpdateZeffDataTable(Material material, List<double> energies)
    {
        var headers = new ObservableCollection<ColumnHeader>
        {
            new ColumnHeader { Title = "Energy (MeV)" },
            new ColumnHeader { Title = "Zeff (Direct)" },
            new ColumnHeader { Title = "Zeff (Interpolated)" }
        };

        var zeffCurve = material.GetEffectiveAtomicNumberCurve();
        var zeffObject = new CrossSectionCurve(0, 0, 0, zeffCurve.Energies, zeffCurve.ZeffValues);
        var totalC = material.GetTotalCrossSectionCurve();
        var totalXsObject = new CrossSectionCurve(0, 0, 0, totalC.Energies, totalC.CrossSections);
        int minZ = material.Composition.Keys.Min(e => e.Z);
        int maxZ = material.Composition.Keys.Max(e => e.Z);

        BuildTableGeneric(material, energies, headers, (e) =>
        {
            var vals = new List<string> { e.ToString("G5") };
            double directZeff = zeffObject.Interpolate(e);
            vals.Add(directZeff.ToString("G6"));
            double st = totalXsObject.Interpolate(e);
            double interpZeff = _ptService.CalculateInterpolatedZeff(e, st, minZ, maxZ, directZeff);
            vals.Add(interpZeff > 0 ? interpZeff.ToString("G6") : "N/A");
            return vals;
        }, _zeffCache, v => ZeffData = v, c => ZeffHeaders = c);
    }

    public void UpdateDepthsDataTable(Material material, List<double> energies, double nValue, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
    {
        var headers = new ObservableCollection<ColumnHeader>
        {
            new ColumnHeader { Title = "Energy (MeV)" },
            new ColumnHeader { Title = "MFP (cm)" }
        };

        bool showNvl = nValue > 1;
        if (showNvl)
        {
            headers.Add(new ColumnHeader { Title = nValue switch { 2.0 => "HVL (cm)", 4.0 => "QVL (cm)", 10.0 => "TVL (cm)", _ => $"1/{nValue:0.##} VL (cm)" } });
        }

        double factor = material.MassAttenuationFactor * (material.Density ?? 0.0);

        BuildTableGeneric(material, energies, headers, (e) =>
        {
            var vals = new List<string> { e.ToString("G5") };
            double sumXs = 0;
            if (incCoh) sumXs += material.GetComponentCrossSection(e, 71);
            if (incInc) sumXs += material.GetComponentCrossSection(e, 72);
            if (incPe) sumXs += material.GetComponentCrossSection(e, 73);
            if (incPair) sumXs += material.GetComponentCrossSection(e, 74);
            if (incTrip) sumXs += material.GetComponentCrossSection(e, 75);
            if (incPn) sumXs += material.GetComponentCrossSection(e, 0);

            double customLac = sumXs * factor;
            vals.Add(customLac > 0 ? (1.0 / customLac).ToString("G6") : "Infinity");
            if (showNvl) vals.Add(customLac > 0 ? (Math.Log(nValue) / customLac).ToString("G6") : "Infinity");
            return vals;
        }, _depthsCache, v => DepthsData = v, c => DepthsHeaders = c);
    }

    public void UpdateBuildupDataTable(Material material, double[] targetMfps, BuildupFactorType type)
    {
        var headers = new ObservableCollection<ColumnHeader>
        {
            new ColumnHeader { Title = "Energy (MeV)" },
            new ColumnHeader { Title = "Zeq" }
        };
        foreach (var mfp in targetMfps) headers.Add(new ColumnHeader { Title = $"{mfp} MFP" });

        BuildTableGeneric(material, BuildupCalculator.AnsiEnergiesMeV, headers, (e) =>
        {
            var vals = new List<string> { e.ToString("G5") };
            double zeq = _buildupCalc.GetEquivalentAtomicNumber(material, e);
            vals.Add(zeq > 0 ? zeq.ToString("G6") : "N/A");

            var gp = _buildupCalc.GetGpParameters(material, e, type);
            foreach (var mfp in targetMfps)
            {
                if (gp != null && gp.A != 0 && zeq > 0) vals.Add(_buildupCalc.CalculateBuildupFactor(material, e, mfp, type).ToString("G6"));
                else vals.Add("-");
            }
            return vals;
        }, _buildupCache, v => BuildupData = v, c => BuildupHeaders = c);
    }

    public void UpdateAbsorptionEdgesTable(Material material)
    {
        if (material == null) { AbsorptionEdgesData = null; return; }
        var list = new List<AbsorptionEdgeRow>();
        foreach (var kvp in material.Composition) list.AddRange(kvp.Key.AbsorptionEdges);
        AbsorptionEdgesData = new ObservableCollection<AbsorptionEdgeRow>(list.OrderByDescending(x => x.EnergyMeV));
    }

    [RelayCommand] private void CopyCrossSectionTable() => CopyToClipboard(CrossSectionData, CrossSectionHeaders);
    [RelayCommand] private void CopyMacTable() => CopyToClipboard(MacData, MacHeaders);
    [RelayCommand] private void CopyLacTable() => CopyToClipboard(LacData, LacHeaders);
    [RelayCommand] private void CopyDepthsTable() => CopyToClipboard(DepthsData, DepthsHeaders);
    [RelayCommand] private void CopyZeffTable() => CopyToClipboard(ZeffData, ZeffHeaders);
    [RelayCommand] private void CopyBuildupTable() => CopyToClipboard(BuildupData, BuildupHeaders);

    [RelayCommand]
    private void CopyAbsorptionEdgesTable()
    {
        if (AbsorptionEdgesData == null || AbsorptionEdgesData.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Element\tSubshell\tEnergy (keV)\tEnergy (MeV)");
        foreach (var row in AbsorptionEdgesData) sb.AppendLine($"{row.Element}\t{row.Subshell}\t{row.EnergyKeVString}\t{row.EnergyMeVString}");
        var dp = new DataPackage();
        dp.SetText(sb.ToString());
        Clipboard.SetContent(dp);
    }

    private void CopyToClipboard(ObservableCollection<PhysicsTableRow>? data, ObservableCollection<ColumnHeader>? headers)
    {
        if (data == null || headers == null || headers.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join("\t", headers.Select(c => c.Title)));
        foreach (var row in data) sb.AppendLine(string.Join("\t", row.Values));
        var dp = new DataPackage();
        dp.SetText(sb.ToString());
        Clipboard.SetContent(dp);
    }
}