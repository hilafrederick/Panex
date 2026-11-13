// File: \Services\BuildupCalculator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Panex.Models;

namespace Panex.Services;

public class BuildupCalculator
{
    private readonly PeriodicTableService _ptService;

    // The 23 standard elements defined by ANSI/ANS-6.4.3
    private readonly int[] _ansiStandardZ = { 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 18, 19, 20, 26, 29, 42, 50, 57, 64, 74, 82, 92 };

    // Changed from readonly so we can lazy-load it
    private Element[]? _ansiElements;

    // The 25 standard energies defined by ANSI
    public static readonly double[] AnsiEnergiesMeV = { 0.015, 0.02, 0.03, 0.04, 0.05, 0.06, 0.08, 0.1, 0.15, 0.2, 0.3, 0.4, 0.5, 0.6, 0.8, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 15.0 };

    // Standard Depths
    public static readonly double[] StandardMfps = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 15, 20, 25, 30, 35, 40 };

    public BuildupCalculator(PeriodicTableService ptService)
    {
        _ptService = ptService;
        // Leave this empty! We will load the elements later when needed.
    }

    // NEW: Lazy-load property. It only queries the periodic table the first time it is used.
    private Element[] AnsiElements
    {
        get
        {
            if (_ansiElements == null || _ansiElements.Length == 0)
            {
                _ansiElements = _ansiStandardZ
                    .Select(z => _ptService.GetAllElements().FirstOrDefault(e => e.Z == z))
                    .Where(e => e != null)
                    .ToArray()!;
            }
            return _ansiElements;
        }
    }

    public double CalculateBuildupFactor(Material material, double energyMeV, double mfp, BuildupFactorType type)
    {
        var gp = GetGpParameters(material, energyMeV, type);
        if (gp == null) return 1.0; // Fallback

        return ApplyGpFormula(gp, mfp);
    }

    public GpParameters? GetGpParameters(Material material, double energyMeV, BuildupFactorType type)
    {
        // 1. Pure element fast-path
        if (material.Composition.Count == 1)
        {
            var pureElement = material.Composition.Keys.First();
            if (_ansiStandardZ.Contains(pureElement.Z))
            {
                var dict = type == BuildupFactorType.Exposure ? pureElement.EbfParameters : pureElement.EabfParameters;
                if (dict.TryGetValue(energyMeV, out var directGp)) return directGp;
            }
        }

        // 2. Equivalent Atomic Number
        double zeq = GetEquivalentAtomicNumber(material, energyMeV);
        if (zeq <= 0) return null;

        // 3. Interpolate Parameters
        return InterpolateGpParameters(zeq, energyMeV, type);
    }

    public double GetEquivalentAtomicNumber(Material material, double energyMeV)
    {
        // ANSI explicitly excludes Coherent (71) and Photonuclear (0).
        // We only want Compton (72), Photoelectric (73), Pair (74), and Triplet (75).
        double matCompton = material.GetComponentCrossSection(energyMeV, 72);
        double matTotalAnsi = matCompton
                            + material.GetComponentCrossSection(energyMeV, 73)
                            + material.GetComponentCrossSection(energyMeV, 74)
                            + material.GetComponentCrossSection(energyMeV, 75);

        if (matTotalAnsi <= 0) return 0;
        double matR = matCompton / matTotalAnsi;

        List<double> possibleZeqs = new();

        double prevR = -1;
        Element? prevEl = null;

        // Step through our pre-resolved elements directly using AnsiElements
        foreach (var el in AnsiElements)
        {
            double elCompton = el.GetPhotoatomicCurve(72, 0)?.Interpolate(energyMeV) ?? 0;
            double elTotalAnsi = elCompton
                               + (el.GetPhotoatomicCurve(73, 0)?.Interpolate(energyMeV) ?? 0)
                               + (el.GetPhotoatomicCurve(74, 0)?.Interpolate(energyMeV) ?? 0)
                               + (el.GetPhotoatomicCurve(75, 0)?.Interpolate(energyMeV) ?? 0);

            double currentR = elTotalAnsi > 0 ? elCompton / elTotalAnsi : 0;

            if (prevEl != null)
            {
                // Check if the material's ratio falls between the previous and current element's ratios
                if ((currentR <= matR && matR <= prevR) || (currentR >= matR && matR >= prevR))
                {
                    double zeq = PhysicsConstants.InterpolateLinearOnLogX(matR, prevR, currentR, prevEl.Z, el.Z);
                    if (!double.IsNaN(zeq)) possibleZeqs.Add(zeq);
                }
            }

            // Carry the current calculations forward to the next loop step to avoid double-math
            prevR = currentR;
            prevEl = el;
        }

        if (possibleZeqs.Count == 0) return 0;
        if (possibleZeqs.Count == 1) return possibleZeqs[0];

        // TIE-BREAKER: Use Direct Zeff to guide the fallback
        double directZeff = material.GetEffectiveAtomicNumber(energyMeV);
        return possibleZeqs.OrderBy(z => Math.Abs(z - directZeff)).First();
    }

    private GpParameters InterpolateGpParameters(double zeq, double energyMeV, BuildupFactorType type)
    {
        // Use our fast AnsiElements array instead of calling GetAllElements()
        var el1 = AnsiElements.LastOrDefault(e => e.Z <= zeq) ?? AnsiElements.First();
        var el2 = AnsiElements.FirstOrDefault(e => e.Z >= zeq) ?? AnsiElements.Last();

        var dict1 = type == BuildupFactorType.Exposure ? el1.EbfParameters : el1.EabfParameters;
        var dict2 = type == BuildupFactorType.Exposure ? el2.EbfParameters : el2.EabfParameters;

        if (!dict1.TryGetValue(energyMeV, out var gp1) || !dict2.TryGetValue(energyMeV, out var gp2))
            return new GpParameters { EnergyMeV = energyMeV };

        // Use shared math helper
        return new GpParameters
        {
            EnergyMeV = energyMeV,
            A = PhysicsConstants.InterpolateLinearOnLogX(zeq, el1.Z, el2.Z, gp1.A, gp2.A),
            B = PhysicsConstants.InterpolateLinearOnLogX(zeq, el1.Z, el2.Z, gp1.B, gp2.B),
            C = PhysicsConstants.InterpolateLinearOnLogX(zeq, el1.Z, el2.Z, gp1.C, gp2.C),
            D = PhysicsConstants.InterpolateLinearOnLogX(zeq, el1.Z, el2.Z, gp1.D, gp2.D),
            Xk = PhysicsConstants.InterpolateLinearOnLogX(zeq, el1.Z, el2.Z, gp1.Xk, gp2.Xk)
        };
    }

    private double ApplyGpFormula(GpParameters p, double x)
    {
        if (x == 0) return 1.0;

        double tanhNeg2 = Math.Tanh(-2.0);
        double K = p.C * Math.Pow(x, p.A) + p.D * ((Math.Tanh((x / p.Xk) - 2.0) - tanhNeg2) / (1.0 - tanhNeg2));

        if (Math.Abs(K - 1.0) > 0.0001)
            return 1.0 + ((p.B - 1.0) * (Math.Pow(K, x) - 1.0) / (K - 1.0));
        else
            return 1.0 + ((p.B - 1.0) * x);
    }
}