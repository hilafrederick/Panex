// File: \Models\CrossSectionCurve.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Panex.Models;

public class CrossSectionCurve
{
    // ENDL Parameters
    public int C { get; set; } // Reaction Descriptor (e.g., 71, 73)
    public int S { get; set; } // Modifier (0 = Total, 91 = Subshell)
    public int X { get; set; } // Subshell Designator (1 = K, etc.)

    public double[] Energies { get; private set; }
    public double[] CrossSections { get; private set; }

    // Constants for Fortran XCOM 3.1 Pair and Triplet Production Thresholds (in MeV)
    public const double XcomPairThresholdNuclear = 1.022007;   // 1.022007E+06 eV (C=74)
    public const double XcomPairThresholdElectron = 2.044014;  // 2.044014E+06 eV (C=75)

    // Interpolation Caches
    public bool IsXcom { get; private set; }
    private readonly NaturalCubicSpline? _spline;
    private readonly double[]? _logEnergies;
    private readonly double[]? _logCrossSections;

    // True physical boundaries to prevent threshold smearing
    private readonly double _minEnergy;
    private readonly double _maxEnergy;
    private readonly double _firstCrossSection;
    private readonly double _maxCrossSection;
    private readonly double _xcomThreshold = 0.0;

    // STRICT XCOM REPLICATION: Hybrid Photoelectric Math Caches
    private readonly List<(double[] LogE, double[] LogXs)>? _xcomPiecewiseGrids;
    private readonly double _xcomEcut = 0; // The highest absorption edge (K-edge)

    public string ReactionName => GetReactionName();
    public string SubshellName => GetSubshellName();

    public CrossSectionCurve(int c, int s, int x, double[] energies, double[] crossSections, bool isXcom = false, List<(double[] E, double[] Xs)>? xcomPiecewiseGrids = null)
    {
        C = c;
        S = s;
        X = x;
        Energies = energies ?? Array.Empty<double>();
        CrossSections = crossSections ?? Array.Empty<double>();
        IsXcom = isXcom;

        if (Energies.Length == 0 || CrossSections.Length == 0)
        {
            _minEnergy = double.MaxValue;
            _maxEnergy = 0;
            _firstCrossSection = 0;
            _maxCrossSection = 0;
            return;
        }

        _minEnergy = Energies[0];
        _maxEnergy = Energies[^1];
        _firstCrossSection = CrossSections[0];
        _maxCrossSection = CrossSections[^1];

        // 1. STRICT XCOM REPLICATION (Log-Log Splines, Threshold-Reduced Variables, & Hybrid Photoelectric)
        if (isXcom)
        {
            // Pair Production (74, 75) uses reduced variables splined against (1 - Eth/E)^3 as in Fortran XCOM 3.1
            if (C == 74 || C == 75)
            {
                _xcomThreshold = C == 74 ? XcomPairThresholdNuclear : XcomPairThresholdElectron;

                var validPoints = Energies.Zip(CrossSections, (E, Xs) => new { E, Xs })
                                          .Where(p => p.E > _xcomThreshold && p.Xs > 0)
                                          .GroupBy(p => p.E)
                                          .Select(g => g.First())
                                          .ToList();

                if (validPoints.Count >= 2)
                {
                    _logEnergies = validPoints.Select(p => Math.Log(p.E)).ToArray();
                    _logCrossSections = validPoints.Select(p =>
                    {
                        double term = (p.E - _xcomThreshold) / p.E;
                        return Math.Log(p.Xs / Math.Pow(term, 3));
                    }).ToArray();

                    _spline = new NaturalCubicSpline(_logEnergies, _logCrossSections);
                }
                return;
            }

            // Clean data for Log-Log space (prevent Math.Log(0) -Infinity crashes)
            // GroupBy deduplicates any coincident energies which crash the Spline matrix
            var validGeneralPoints = Energies.Zip(CrossSections, (E, Xs) => new { E, Xs })
                                              .Where(p => p.E > 0 && p.Xs > 0)
                                              .GroupBy(p => p.E)
                                              .Select(g => g.First())
                                              .ToList();

            if (validGeneralPoints.Count == 0)
            {
                _logEnergies = Array.Empty<double>();
                _logCrossSections = Array.Empty<double>();
                return;
            }

            _minEnergy = validGeneralPoints.First().E;
            _maxEnergy = validGeneralPoints.Last().E;
            _firstCrossSection = validGeneralPoints.First().Xs;
            _maxCrossSection = validGeneralPoints.Last().Xs;

            _logEnergies = validGeneralPoints.Select(p => Math.Log(p.E)).ToArray();
            _logCrossSections = validGeneralPoints.Select(p => Math.Log(p.Xs)).ToArray();

            // Photoelectric Hybrid Setup
            if (xcomPiecewiseGrids != null && xcomPiecewiseGrids.Count > 0)
            {
                _xcomPiecewiseGrids = xcomPiecewiseGrids.Select(g => (
                    g.E.Select(x => Math.Log(x)).ToArray(),
                    g.Xs.Select(x => Math.Log(x)).ToArray()
                )).ToList();

                _xcomEcut = xcomPiecewiseGrids.Last().E.Last();

                // Use a 1-part-in-a-million tolerance to safely grab the top of the cliff
                var truncatedGrid = Energies.Zip(CrossSections, (E, Xs) => new { E, Xs })
                                            .Where(p => p.E > 0 && p.Xs > 0 && p.E >= _xcomEcut * 0.999999)
                                            .GroupBy(p => Math.Round(p.E, 6))
                                            .Select(g => g.Last())
                                            .ToList();
                if (truncatedGrid.Count >= 2)
                {
                    _spline = new NaturalCubicSpline(
                        truncatedGrid.Select(p => Math.Log(p.E)).ToArray(),
                        truncatedGrid.Select(p => Math.Log(p.Xs)).ToArray()
                    );
                }
            }
            else if (_logEnergies.Length >= 2)
            {
                // Scattering -> Log-Log Cubic Spline
                _spline = new NaturalCubicSpline(_logEnergies, _logCrossSections);
            }
        }
        // 2. STANDARD EPDL / LINEAR-LINEAR (Lin-Lin on raw axes, no log/spline allocation)
    }

    public double Interpolate(double energyMeV)
    {
        if (Energies.Length == 0 || CrossSections.Length == 0) return 0.0;

        if (energyMeV < _minEnergy)
        {
            // Pair Production (74, 75) and Photonuclear (0) have physical thresholds.
            if (C == 74 || C == 75 || C == 0) return 0.0;

            // the _minEnergy is a library limitation. Flatline to the boundary value to prevent 0.0 drops!
            return _firstCrossSection;
        }

        // Flatline at the maximum if requested above the grid
        if (energyMeV >= _maxEnergy) return _maxCrossSection;

        // --- 1. STANDARD EPDL ROUTING (Linear Interpolation on Linear-Linear Axes) ---
        if (!IsXcom)
        {
            return LinearLinear(energyMeV, Energies, CrossSections);
        }

        double targetLogE = Math.Log(energyMeV);

        // --- 2. STRICT XCOM REPLICATION ROUTING (Log-Log Space & Reduced Variables) ---
        if (C == 74 || C == 75)
        {
            if (energyMeV <= _xcomThreshold) return 0.0;

            if (_spline != null)
            {
                double res = _spline.Evaluate(targetLogE);
                double term = (energyMeV - _xcomThreshold) / energyMeV;
                return Math.Pow(term, 3) * Math.Exp(res);
            }
            return 0.0;
        }

        if (_logEnergies == null || _logEnergies.Length == 0) return 0.0;

        // Photoelectric Hybrid Routing
        if (_xcomPiecewiseGrids != null)
        {
            if (targetLogE < _xcomPiecewiseGrids.Last().LogE.Last() - 1e-10)
            {
                var grid = _xcomPiecewiseGrids.FirstOrDefault(g => targetLogE < g.LogE.Last() - 1e-10);
                if (grid.LogE == null || grid.LogXs == null) grid = _xcomPiecewiseGrids.Last(); // Fallback

                return Math.Exp(LogLogLinear(targetLogE, grid.LogE, grid.LogXs));
            }
            else
            {
                // Above K-Edge -> Use the truncated Spline
                return _spline != null ? Math.Exp(_spline.Evaluate(targetLogE)) : 0.0;
            }
        }

        // Scattering -> Log-Log Cubic Spline
        if (_spline != null)
        {
            return Math.Exp(_spline.Evaluate(targetLogE));
        }

        if (_logCrossSections == null) return 0.0;
        return Math.Exp(LogLogLinear(targetLogE, _logEnergies, _logCrossSections));
    }

    // Standard Linear-Linear interpolation on raw (E, Xs) scale for EPDL and generic curves
    private static double LinearLinear(double targetE, double[] eGrid, double[] xsGrid)
    {
        if (targetE <= eGrid[0]) return xsGrid[0];
        if (targetE >= eGrid[^1]) return xsGrid[^1];

        int index = Array.BinarySearch(eGrid, targetE);
        if (index >= 0)
        {
            while (index + 1 < eGrid.Length && Math.Abs(eGrid[index + 1] - eGrid[index]) <= 1e-12 * Math.Max(1e-10, targetE))
            {
                index++;
            }
            return xsGrid[index]; // Exact match (post-edge knot if duplicate)
        }

        int nextIndex = ~index;
        int prevIndex = nextIndex - 1;

        // If targetE is within floating-point tolerance of an edge knot, snap to post-edge knot
        if (nextIndex < eGrid.Length && Math.Abs(targetE - eGrid[nextIndex]) <= 1e-9 * targetE)
        {
            int idx = nextIndex;
            while (idx + 1 < eGrid.Length && Math.Abs(eGrid[idx + 1] - eGrid[idx]) <= 1e-12 * Math.Max(1e-10, targetE))
            {
                idx++;
            }
            return xsGrid[idx];
        }

        if (prevIndex >= 0 && Math.Abs(targetE - eGrid[prevIndex]) <= 1e-9 * targetE)
        {
            int idx = prevIndex;
            while (idx + 1 < eGrid.Length && Math.Abs(eGrid[idx + 1] - eGrid[idx]) <= 1e-12 * Math.Max(1e-10, targetE))
            {
                idx++;
            }
            return xsGrid[idx];
        }

        double e0 = eGrid[prevIndex];
        double e1 = eGrid[nextIndex];
        double y0 = xsGrid[prevIndex];
        double y1 = xsGrid[nextIndex];

        if (e1 <= e0) return y0;

        return y0 + (targetE - e0) * ((y1 - y0) / (e1 - e0));
    }

    // Extracted log-log linear interpolation logic for XCOM
    private static double LogLogLinear(double targetLogE, double[] logEGrid, double[] logXsGrid)
    {
        // Clamp boundaries to prevent IndexOutOfRangeException from floating-point drift
        if (targetLogE <= logEGrid[0]) return logXsGrid[0];
        if (targetLogE >= logEGrid[^1]) return logXsGrid[^1];

        int index = Array.BinarySearch(logEGrid, targetLogE);
        if (index >= 0) return logXsGrid[index]; // Exact match

        int nextIndex = ~index;
        int prevIndex = nextIndex - 1;

        double x0 = logEGrid[prevIndex];
        double x1 = logEGrid[nextIndex];
        double y0 = logXsGrid[prevIndex];
        double y1 = logXsGrid[nextIndex];

        if (x1 <= x0) return y0;

        return y0 + (targetLogE - x0) * ((y1 - y0) / (x1 - x0));
    }

    private string GetReactionName()
    {
        return C switch
        {
            71 => "Coherent Scattering",
            72 => "Incoherent Scattering",
            73 => "Photoelectric Effect",
            74 => "Pair Production (Nuclear Field)",
            75 => "Pair Production (Electron Field)",
            _ => $"Unknown Reaction (C={C})"
        };
    }

    private string GetSubshellName()
    {
        if (S != 91) return "Total";

        return X switch
        {
            1 => "K (1s1/2)",
            2 => "L (2)",
            3 => "L1 (2s1/2)",
            4 => "L23 (2p)",
            5 => "L2 (2p1/2)",
            6 => "L3 (2p3/2)",
            7 => "M (3)",
            8 => "M1 (3s1/2)",
            9 => "M23 (3p)",
            10 => "M2 (3p1/2)",
            11 => "M3 (3p3/2)",
            12 => "M45 (3d)",
            13 => "M4 (3d3/2)",
            14 => "M5 (3d5/2)",
            15 => "N (4)",
            16 => "N1 (4s1/2)",
            17 => "N23 (4p)",
            18 => "N2 (4p1/2)",
            19 => "N3 (4p3/2)",
            20 => "N45 (4d)",
            21 => "N4 (4d3/2)",
            22 => "N5 (4d5/2)",
            23 => "N67 (4f)",
            24 => "N6 (4f5/2)",
            25 => "N7 (4f7/2)",
            26 => "O (5)",
            27 => "O1 (5s1/2)",
            28 => "O23 (5p)",
            29 => "O2 (5p1/2)",
            30 => "O3 (5p3/2)",
            31 => "O45 (5d)",
            32 => "O4 (5d3/2)",
            33 => "O5 (5d5/2)",
            34 => "O67 (5f)",
            35 => "O6 (5f5/2)",
            36 => "O7 (5f7/2)",
            37 => "O89 (5g)",
            38 => "O8 (5g7/2)",
            39 => "O9 (5g9/2)",
            40 => "P (6)",
            41 => "P1 (6s1/2)",
            42 => "P23 (6p)",
            43 => "P2 (6p1/2)",
            44 => "P3 (6p3/2)",
            45 => "P45 (6d)",
            46 => "P4 (6d3/2)",
            47 => "P5 (6d5/2)",
            48 => "P67 (6f)",
            49 => "P6 (6f5/2)",
            50 => "P7 (6f7/2)",
            51 => "P89 (6g)",
            52 => "P8 (6g7/2)",
            53 => "P9 (6g9/2)",
            54 => "P1011 (6h)",
            55 => "P10 (6h9/2)",
            56 => "P11 (6h11/2)",
            57 => "Q (7)",
            58 => "Q1 (7s1/2)",
            59 => "Q23 (7p)",
            60 => "Q2 (7p1/2)",
            61 => "Q3 (7p3/2)",
            _ => $"Subshell X={X}"
        };
    }
}