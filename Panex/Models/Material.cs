using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Panex.Models;

public partial class Material : ObservableObject
{
    [ObservableProperty]
    private string name = "New Material";

    [ObservableProperty]
    private bool isCompared = false;

    public string Category { get; set; } = "Uncategorized";
    public string Reference { get; set; } = string.Empty;
    public string Citation { get; set; } = string.Empty;
    public double? Density { get; set; }
    public Dictionary<Element, double> Composition { get; set; } = new();

    public string CompositionString => string.Join(", ", Composition.Select(x => $"{x.Key.Symbol}: {x.Value:P2}"));

    // Atom-fraction-weighted average atomic mass
    public double AverageAtomicMass => Composition.Sum(x => x.Key.AtomicMass * x.Value);

    // Conversion factor (barns/atom -> cm²/g)
    public double MassAttenuationFactor
    {
        get
        {
            if (AverageAtomicMass <= 0) return 0.0;
            return (PhysicsConstants.Avogadro * PhysicsConstants.BarnsToCm2) / AverageAtomicMass;
        }
    }

    public double GetCrossSection(double energyMeV)
    {
        double total = 0;
        foreach (var kvp in Composition)
        {
            double fraction = kvp.Value;
            total += fraction * kvp.Key.GetTotalCrossSection(energyMeV);
        }
        return total;
    }

    private readonly object _cacheLock = new();

    private double[]? _masterEnergyGridCache;
    private (double[] Energies, double[] CrossSections)? _totalCrossSectionCache;
    private (double[] Energies, double[] CrossSections)? _photonuclearCrossSectionCache;
    private (double[] Energies, double[] ZeffValues)? _zeffCache;

    private ConcurrentDictionary<(int C, int S), (double[] Energies, double[] CrossSections)> _partialCrossSectionCache = new();

    public (double[] Energies, double[] CrossSections) GetCrossSectionCurve(int c, int s, CancellationToken token = default)
    {
        if (_partialCrossSectionCache.TryGetValue((c, s), out var cached)) return cached;

        var allEnergies = new HashSet<double>();

        foreach (var element in Composition.Keys)
        {
            var curve = element.GetPhotoatomicCurve(c, s);
            if (curve != null)
            {
                foreach (var e in curve.Energies) allEnergies.Add(e);
            }
        }

        if (allEnergies.Count == 0) return (Array.Empty<double>(), Array.Empty<double>());

        var sortedEnergies = allEnergies.OrderBy(x => x).ToArray();
        var resultValues = new double[sortedEnergies.Length];

        Parallel.For(0, sortedEnergies.Length, new ParallelOptions { CancellationToken = token }, i =>
        {
            double energy = sortedEnergies[i];
            double weightedSum = 0;

            foreach (var kvp in Composition)
            {
                var curve = kvp.Key.GetPhotoatomicCurve(c, s);
                if (curve != null)
                {
                    weightedSum += kvp.Value * curve.Interpolate(energy);
                }
            }

            resultValues[i] = weightedSum;
        });

        var result = (sortedEnergies, resultValues);
        _partialCrossSectionCache.TryAdd((c, s), result);
        return result;
    }

    public (double[] Energies, double[] CrossSections) GetPhotonuclearCrossSectionCurve()
    {
        lock (_cacheLock)
        {
            if (_photonuclearCrossSectionCache != null) return _photonuclearCrossSectionCache.Value;
        }

        var allEnergies = new SortedSet<double>();
        foreach (var element in Composition.Keys)
        {
            var curve = element.GetElementalPhotonuclearCurve();
            foreach (var e in curve.Energies) allEnergies.Add(e);
        }

        if (allEnergies.Count == 0) return (Array.Empty<double>(), Array.Empty<double>());

        var sortedEnergies = allEnergies.ToArray();
        var resultValues = new double[sortedEnergies.Length];

        for (int i = 0; i < sortedEnergies.Length; i++)
        {
            double energy = sortedEnergies[i];
            double weightedSum = 0;

            foreach (var kvp in Composition)
            {
                weightedSum += kvp.Value * kvp.Key.GetElementalPhotonuclearCurve().Interpolate(energy);
            }
            resultValues[i] = weightedSum;
        }

        lock (_cacheLock)
        {
            _photonuclearCrossSectionCache = (sortedEnergies, resultValues);
            return _photonuclearCrossSectionCache.Value;
        }
    }

    public (double[] Energies, double[] CrossSections) GetTotalCrossSectionCurve(CancellationToken token = default)
    {
        lock (_cacheLock)
        {
            if (_totalCrossSectionCache != null) return _totalCrossSectionCache.Value;
        }

        var allEnergies = new SortedSet<double>();
        int[] relevantCs = { 71, 72, 73, 74, 75 };

        foreach (var element in Composition.Keys)
        {
            foreach (var curve in element.PhotoatomicData.Where(x => relevantCs.Contains(x.C) && x.S == 0))
                foreach (var e in curve.Energies) allEnergies.Add(e);

            var pnCurve = element.GetElementalPhotonuclearCurve();
            foreach (var e in pnCurve.Energies) allEnergies.Add(e);
        }

        if (allEnergies.Count == 0) return (Array.Empty<double>(), Array.Empty<double>());

        var sortedEnergies = allEnergies.ToArray();
        var resultValues = new double[sortedEnergies.Length];

        Parallel.For(0, sortedEnergies.Length, new ParallelOptions { CancellationToken = token }, i =>
        {
            double e = sortedEnergies[i];
            double matTotal = 0;

            foreach (var kvp in Composition)
            {
                var el = kvp.Key;
                double frac = kvp.Value;

                double elAtomic = 0;
                elAtomic += InterpolateSpecific(el, 71, 0, e);
                elAtomic += InterpolateSpecific(el, 72, 0, e);
                elAtomic += InterpolateSpecific(el, 73, 0, e);
                elAtomic += InterpolateSpecific(el, 74, 0, e);
                elAtomic += InterpolateSpecific(el, 75, 0, e);

                double elNuclear = el.GetElementalPhotonuclearCurve().Interpolate(e);

                matTotal += frac * (elAtomic + elNuclear);
            }

            resultValues[i] = matTotal;
        });

        lock (_cacheLock)
        {
            _totalCrossSectionCache = (sortedEnergies, resultValues);
            return _totalCrossSectionCache.Value;
        }
    }

    public double GetComponentCrossSection(double energyMeV, int c)
    {
        if (c == 0) // Photonuclear
        {
            double pn = 0.0;
            foreach (var kvp in Composition)
                pn += kvp.Value * kvp.Key.GetElementalPhotonuclearCurve().Interpolate(energyMeV);
            return pn;
        }

        double sum = 0.0;
        foreach (var kvp in Composition)
        {
            var curve = kvp.Key.GetPhotoatomicCurve(c, 0);
            if (curve != null)
                sum += kvp.Value * curve.Interpolate(energyMeV);
        }
        return sum;
    }

    public (double[] Energies, double[] MacValues) GetMassAttenuationCurve(int c, int s)
    {
        var (energies, xs) = GetCrossSectionCurve(c, s);
        double factor = MassAttenuationFactor;
        var macs = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++) macs[i] = xs[i] * factor;
        return (energies, macs);
    }

    public (double[] Energies, double[] MacValues) GetTotalMassAttenuationCurve()
    {
        var (energies, xs) = GetTotalCrossSectionCurve();
        double factor = MassAttenuationFactor;
        var macs = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++) macs[i] = xs[i] * factor;
        return (energies, macs);
    }

    public (double[] Energies, double[] MacValues) GetPhotonuclearMassAttenuationCurve()
    {
        var (energies, xs) = GetPhotonuclearCrossSectionCurve();
        double factor = MassAttenuationFactor;
        var macs = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++) macs[i] = xs[i] * factor;
        return (energies, macs);
    }

    private double InterpolateSpecific(Element el, int c, int s, double energy, int x = 0)
    {
        var curve = el.GetPhotoatomicCurve(c, s, x);
        return curve?.Interpolate(energy) ?? 0.0;
    }

    public double[] GetMasterEnergyGrid()
    {
        lock (_cacheLock)
        {
            if (_masterEnergyGridCache != null) return _masterEnergyGridCache;
        }

        var allEnergies = new SortedSet<double>();
        int[] relevantCs = { 71, 72, 73, 74, 75 };

        foreach (var element in Composition.Keys)
        {
            foreach (var curve in element.PhotoatomicData.Where(x => relevantCs.Contains(x.C) && x.S == 0))
                foreach (var e in curve.Energies) allEnergies.Add(e);

            var pnCurve = element.GetElementalPhotonuclearCurve();
            foreach (var e in pnCurve.Energies) allEnergies.Add(e);

            foreach (var edge in element.AbsorptionEdges)
            {
                allEnergies.Add(edge.EnergyMeV * 0.999999);
                allEnergies.Add(edge.EnergyMeV);
                allEnergies.Add(edge.EnergyMeV * 1.000001);
            }
        }

        lock (_cacheLock)
        {
            _masterEnergyGridCache = allEnergies.ToArray();
            return _masterEnergyGridCache;
        }
    }

    public IEnumerable<(string Label, double[] Energies, double[] CrossSections)> GetPartialPhotoelectricCurves()
    {
        foreach (var kvp in Composition)
        {
            Element element = kvp.Key;
            double fraction = kvp.Value;

            var partials = element.PhotoatomicData.Where(x => x.C == 73 && x.S == 91);

            foreach (var curve in partials)
            {
                var scaledXs = new double[curve.CrossSections.Length];
                for (int i = 0; i < curve.CrossSections.Length; i++)
                {
                    scaledXs[i] = curve.CrossSections[i] * fraction;
                }

                string label = $"{element.Symbol} - {curve.SubshellName}";
                yield return (label, curve.Energies, scaledXs);
            }
        }
    }

    public IEnumerable<(string Label, double[] Energies, double[] CrossSections)> GetIsotopicPhotonuclearCurves()
    {
        foreach (var kvp in Composition)
        {
            Element element = kvp.Key;
            double matFraction = kvp.Value;

            foreach (var iso in element.Isotopes)
            {
                if (iso.PhotonuclearData != null && iso.PhotonuclearData.Energies.Length > 0)
                {
                    double totalFactor = matFraction * iso.Abundance;
                    var raw = iso.PhotonuclearData;
                    var scaledXS = new double[raw.CrossSections.Length];
                    for (int i = 0; i < raw.CrossSections.Length; i++)
                    {
                        scaledXS[i] = raw.CrossSections[i] * totalFactor;
                    }

                    string label = $"{element.Symbol}-{iso.A} (PN)";
                    yield return (label, raw.Energies, scaledXS);
                }
            }
        }
    }

    public double GetEffectiveAtomicNumber(double energyMeV)
    {
        double sigmaAtomic = 0.0;
        double sigmaElectronic = 0.0;

        foreach (var kvp in Composition)
        {
            Element element = kvp.Key;
            double atomFraction = kvp.Value;

            double sig_i = element.GetTotalCrossSection(energyMeV);

            sigmaAtomic += atomFraction * sig_i;

            if (element.Z > 0)
            {
                sigmaElectronic += atomFraction * (sig_i / element.Z);
            }
        }

        return sigmaElectronic > 0 ? sigmaAtomic / sigmaElectronic : 0.0;
    }

    public (double[] Energies, double[] ZeffValues) GetEffectiveAtomicNumberCurve(CancellationToken token = default)
    {
        lock (_cacheLock)
        {
            if (_zeffCache != null) return _zeffCache.Value;
        }

        var energies = GetMasterEnergyGrid();
        var zeffValues = new double[energies.Length];

        Parallel.For(0, energies.Length, new ParallelOptions { CancellationToken = token }, i =>
        {
            double e = energies[i];
            double sigmaAtomic = 0.0;
            double sigmaElectronic = 0.0;

            foreach (var kvp in Composition)
            {
                var el = kvp.Key;
                var fraction = kvp.Value;

                double sig_i = 0.0;
                sig_i += el.GetPhotoatomicCurve(71, 0)?.Interpolate(e) ?? 0.0;
                sig_i += el.GetPhotoatomicCurve(72, 0)?.Interpolate(e) ?? 0.0;
                sig_i += el.GetPhotoatomicCurve(73, 0)?.Interpolate(e) ?? 0.0;
                sig_i += el.GetPhotoatomicCurve(74, 0)?.Interpolate(e) ?? 0.0;
                sig_i += el.GetPhotoatomicCurve(75, 0)?.Interpolate(e) ?? 0.0;
                sig_i += el.GetElementalPhotonuclearCurve().Interpolate(e);

                sigmaAtomic += fraction * sig_i;
                if (el.Z > 0) sigmaElectronic += fraction * (sig_i / el.Z);
            }

            zeffValues[i] = sigmaElectronic > 0 ? sigmaAtomic / sigmaElectronic : 0.0;
        });

        lock (_cacheLock)
        {
            _zeffCache = (energies, zeffValues);
            return _zeffCache.Value;
        }
    }

    public double GetCustomTotalCrossSection(double energyMeV, bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn)
    {
        double sumXs = 0;
        if (incCoh) sumXs += GetComponentCrossSection(energyMeV, 71);
        if (incInc) sumXs += GetComponentCrossSection(energyMeV, 72);
        if (incPe) sumXs += GetComponentCrossSection(energyMeV, 73);
        if (incPair) sumXs += GetComponentCrossSection(energyMeV, 74);
        if (incTrip) sumXs += GetComponentCrossSection(energyMeV, 75);
        if (incPn) sumXs += GetComponentCrossSection(energyMeV, 0);
        return sumXs;
    }

    public (double[] Energies, double[] Values) GetCustomTotalCurve(bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn, CancellationToken token = default)
    {
        var energies = GetMasterEnergyGrid();
        var values = new double[energies.Length];

        Parallel.For(0, energies.Length, new ParallelOptions { CancellationToken = token }, i =>
        {
            values[i] = GetCustomTotalCrossSection(energies[i], incPe, incCoh, incInc, incPair, incTrip, incPn);
        });

        return (energies, values);
    }

    public (double[] Energies, double[] ZeffValues) GetCustomZeffCurve(bool incPe, bool incCoh, bool incInc, bool incPair, bool incTrip, bool incPn, CancellationToken token = default)
    {
        var energies = GetMasterEnergyGrid();
        var zeffValues = new double[energies.Length];

        Parallel.For(0, energies.Length, new ParallelOptions { CancellationToken = token }, i =>
        {
            double e = energies[i];
            double sigmaAtomic = 0.0;
            double sigmaElectronic = 0.0;

            foreach (var kvp in Composition)
            {
                var el = kvp.Key;
                var fraction = kvp.Value;

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

            zeffValues[i] = sigmaElectronic > 0 ? sigmaAtomic / sigmaElectronic : 0.0;
        });

        return (energies, zeffValues);
    }

    public void ClearCaches()
    {
        lock (_cacheLock)
        {
            _masterEnergyGridCache = null;
            _totalCrossSectionCache = null;
            _photonuclearCrossSectionCache = null;
            _zeffCache = null;
            _partialCrossSectionCache.Clear();
        }
    }
}