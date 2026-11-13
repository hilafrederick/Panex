// File: \Models\Element.cs
using Panex.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Panex.Models;

public class Element
{
    public int Z { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double AtomicMass { get; set; }
    public List<Isotope> Isotopes { get; set; } = new();

    public List<CrossSectionCurve> PhotoatomicData { get; set; } = new();
    public List<AbsorptionEdgeRow> AbsorptionEdges { get; set; } = new();

    // =========================================================================
    // THREAD-SAFE CACHE FIELDS 
    // =========================================================================
    private readonly object _cacheLock = new();

    // Upgraded to ConcurrentDictionary
    private ConcurrentDictionary<(int C, int S, int X), CrossSectionCurve>? _photoatomicCache;
    private CrossSectionCurve? _elementalPhotonuclearCurve;

    // KEY = Energy in MeV, VALUE = The 5 parameters
    public Dictionary<double, GpParameters> EbfParameters { get; set; } = new();
    public Dictionary<double, GpParameters> EabfParameters { get; set; } = new();

    public string DisplayName => $"[{Z}] {Name} ({Symbol})";

    public CrossSectionCurve? GetPhotoatomicCurve(int c, int s, int x = 0)
    {
        // Double-check locking pattern to safely initialize the dictionary
        if (_photoatomicCache == null)
        {
            lock (_cacheLock)
            {
                if (_photoatomicCache == null)
                {
                    var dict = PhotoatomicData.ToDictionary(k => (k.C, k.S, k.X));
                    _photoatomicCache = new ConcurrentDictionary<(int C, int S, int X), CrossSectionCurve>(dict);
                }
            }
        }

        _photoatomicCache.TryGetValue((c, s, x), out var curve);
        return curve;
    }

    public void RecalculateAtomicMass()
    {
        if (Isotopes.Any())
        {
            double totalAbundance = Isotopes.Sum(i => i.Abundance);
            if (totalAbundance > 0)
            {
                AtomicMass = Isotopes.Sum(i => i.Mass * i.Abundance) / totalAbundance;
            }
        }
    }

    public CrossSectionCurve GetElementalPhotonuclearCurve()
    {
        // 1. Thread-Safe Quick Read
        lock (_cacheLock)
        {
            if (_elementalPhotonuclearCurve != null) return _elementalPhotonuclearCurve;
        }

        // 2. Gather all isotopes with data
        var validIsotopes = Isotopes.Where(i => i.PhotonuclearData != null && i.PhotonuclearData.Energies.Length > 0).ToList();

        if (validIsotopes.Count == 0)
        {
            var emptyCurve = new CrossSectionCurve(0, 0, 0, Array.Empty<double>(), Array.Empty<double>());
            lock (_cacheLock)
            {
                _elementalPhotonuclearCurve ??= emptyCurve;
                return _elementalPhotonuclearCurve;
            }
        }

        // 3. Union all Energy Grids to preserve resolution
        var allEnergies = new SortedSet<double>();
        foreach (var iso in validIsotopes)
        {
            foreach (var e in iso.PhotonuclearData!.Energies) allEnergies.Add(e);
        }

        double[] unifiedEnergies = allEnergies.ToArray();
        double[] weightedXS = new double[unifiedEnergies.Length];

        // 4. Calculate Weighted Sum at every energy point
        for (int i = 0; i < unifiedEnergies.Length; i++)
        {
            double e = unifiedEnergies[i];
            double sum = 0;

            foreach (var iso in validIsotopes)
            {
                sum += iso.Abundance * iso.GetPhotonuclearCrossSection(e);
            }
            weightedXS[i] = sum;
        }

        // 5. Thread-Safe Write
        lock (_cacheLock)
        {
            _elementalPhotonuclearCurve ??= new CrossSectionCurve(0, 0, 0, unifiedEnergies, weightedXS);
            return _elementalPhotonuclearCurve;
        }
    }

    public double GetTotalCrossSection(double energyMeV)
    {
        // 1. Photonuclear
        double photonuclear = GetElementalPhotonuclearCurve().Interpolate(energyMeV);

        // 2. Photoatomic
        double photoatomic = 0.0;
        if (PhotoatomicData != null)
        {
            var relevant = PhotoatomicData.Where(c => c.S == 0 && (c.C >= 71 && c.C <= 75));
            foreach (var c in relevant) photoatomic += c.Interpolate(energyMeV);
        }

        return photonuclear + photoatomic;
    }

    public IEnumerable<(string SubshellName, double EdgeEnergyMeV)> GetAbsorptionEdges()
    {
        if (PhotoatomicData == null) yield break;

        var subshells = PhotoatomicData.Where(c => c.C == 73 && c.S == 91);

        foreach (var curve in subshells)
        {
            if (curve.Energies.Length > 0)
            {
                yield return (curve.SubshellName, curve.Energies[0]);
            }
        }
    }

    public void ClearPhotoatomicCache()
    {
        lock (_cacheLock)
        {
            _photoatomicCache = null;
            _elementalPhotonuclearCurve = null; // Also clear this in case underlying PN data changes
        }
    }
}