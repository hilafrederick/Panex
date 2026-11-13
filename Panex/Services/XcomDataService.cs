// File: \Services\XcomDataService.cs
using Panex.Models;
using Panex.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Panex.Services;

public class XcomDataService : IPhotoatomicDataService
{
    public void LoadCrossSections(IEnumerable<Element> elements, string rootPath)
    {
        if (!Directory.Exists(rootPath)) return;

        foreach (var element in elements)
        {
            string filePath = Path.Combine(rootPath, $"MDATX3.{element.Z:D3}");

            if (File.Exists(filePath))
            {
                // Pass the element into the parser so it can attach the absorption edges
                element.PhotoatomicData = ParseMdatx3(filePath, element);
            }
        }
    }

    private List<CrossSectionCurve> ParseMdatx3(string filePath, Element element)
    {
        var curves = new List<CrossSectionCurve>();
        string text = File.ReadAllText(filePath);
        string[] tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int cursor = 0;

        cursor += 2; // Skip Z and Atomic Weight

        int maxEdg = int.Parse(tokens[cursor++]);
        int maxE = int.Parse(tokens[cursor++]);

        // NEW: Parse the explicitly defined Absorption Edges right out of the XCOM file!
        if (maxEdg > 0)
        {
            cursor += maxEdg; // Skip IDG indices (internal Fortran array pointers)

            string[] labels = new string[maxEdg];
            for (int i = 0; i < maxEdg; i++) labels[i] = tokens[cursor++]; // Read ADG labels (e.g. "K", "L1")

            double[] edgeEnergies_eV = new double[maxEdg];
            for (int i = 0; i < maxEdg; i++) edgeEnergies_eV[i] = double.Parse(tokens[cursor++], CultureInfo.InvariantCulture);

            for (int i = 0; i < maxEdg; i++)
            {
                element.AbsorptionEdges.Add(new AbsorptionEdgeRow
                {
                    Element = element.Symbol,
                    Subshell = labels[i].Trim(),
                    EnergyMeV = edgeEnergies_eV[i] * 1.0e-6 // Convert eV to MeV
                });
            }
        }

        // Extract the main interaction arrays
        double[] energies_eV = ExtractArray(tokens, ref cursor, maxE);
        double[] coherent = ExtractArray(tokens, ref cursor, maxE);
        double[] incoherent = ExtractArray(tokens, ref cursor, maxE);
        double[] photoelectric = ExtractArray(tokens, ref cursor, maxE);
        double[] pairNuclear = ExtractArray(tokens, ref cursor, maxE);
        double[] pairElectron = ExtractArray(tokens, ref cursor, maxE);

        double[] energies_MeV = energies_eV.Select(e => e * 1.0e-6).ToArray();

        // Gather the trailing Piecewise Grids used for Photoelectric hybrid interpolation
        var piecewiseGrids = new List<(double[] E, double[] Xs)>();
        if (cursor < tokens.Length)
        {
            int lax = int.Parse(tokens[cursor++]);
            int[] kmx = new int[lax];

            for (int l = 0; l < lax; l++) kmx[l] = int.Parse(tokens[cursor++]);

            double[][] pEnergies = new double[lax][];
            for (int l = 0; l < lax; l++)
            {
                // THE FIX: These piecewise energies are ALREADY in MeV! 
                // Do NOT multiply by 1.0e-6!
                pEnergies[l] = ExtractArray(tokens, ref cursor, kmx[l]);
            }

            double[][] pCrossSections = new double[lax][];
            for (int l = 0; l < lax; l++) pCrossSections[l] = ExtractArray(tokens, ref cursor, kmx[l]);

            for (int l = 0; l < lax; l++) piecewiseGrids.Add((pEnergies[l], pCrossSections[l]));
        }

        // Map standard interactions to ENDL C-codes with isXcom: true.
        curves.Add(new CrossSectionCurve(71, 0, 0, energies_MeV, coherent, isXcom: true));
        curves.Add(new CrossSectionCurve(72, 0, 0, energies_MeV, incoherent, isXcom: true));
        curves.Add(new CrossSectionCurve(73, 0, 0, energies_MeV, photoelectric, isXcom: true, xcomPiecewiseGrids: piecewiseGrids));
        curves.Add(new CrossSectionCurve(74, 0, 0, energies_MeV, pairNuclear, isXcom: true));
        curves.Add(new CrossSectionCurve(75, 0, 0, energies_MeV, pairElectron, isXcom: true));

        return curves;
    }

    private double[] ExtractArray(string[] tokens, ref int cursor, int length)
    {
        double[] arr = new double[length];
        for (int i = 0; i < length; i++)
        {
            arr[i] = double.Parse(tokens[cursor++], NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return arr;
    }
}