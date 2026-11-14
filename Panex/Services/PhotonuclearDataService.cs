using Panex.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Panex.Services;

public class PhotonuclearDataService
{
    public const string UnifiedFileName = "pn_jendl5_xs.csv";

    // Matches ZA001002.csv (ground state) and optional isomer suffixes like ZA073180m.csv / ZA073180m1.csv
    private static readonly Regex FilePattern = new Regex(
        @"^ZA(\d{3})(\d{3})(?:m(\d*))?\.csv$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool TryParseNuclideFileName(string fileName, out int z, out int a, out int isomerLevel)
    {
        z = 0;
        a = 0;
        isomerLevel = 0;

        var match = FilePattern.Match(fileName);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedZ) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedA))
        {
            return false;
        }

        int parsedIso = 0;
        Group isoGroup = match.Groups[3];
        if (isoGroup.Success && !string.IsNullOrEmpty(isoGroup.Value))
        {
            if (!int.TryParse(isoGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out parsedIso))
            {
                return false;
            }
        }
        else if (isoGroup.Success)
        {
            parsedIso = 1;
        }

        z = parsedZ;
        a = parsedA;
        isomerLevel = parsedIso;
        return true;
    }

    /// <summary>
    /// Orders candidate files when both ground state and isomer curves exist for a natural (Z, A).
    /// Primordial Ta-180 (ZA=73180) is Ta-180m (isomerLevel=1); all other nuclides prefer ground state (0).
    /// </summary>
    private static (int Rank, int IsomerLevel, string FileName) GetSelectionPriority(
        int z, int a, int isomerLevel, string fileName)
    {
        int rank = (z == 73 && a == 180)
            ? (isomerLevel == 1 ? 0 : isomerLevel == 0 ? 1 : 2)
            : (isomerLevel == 0 ? 0 : 1);

        return (rank, isomerLevel, fileName);
    }

    private static int ComparePriority(
        (int Rank, int IsomerLevel, string FileName) left,
        (int Rank, int IsomerLevel, string FileName) right)
    {
        int rankCmp = left.Rank.CompareTo(right.Rank);
        if (rankCmp != 0) return rankCmp;

        int isoCmp = left.IsomerLevel.CompareTo(right.IsomerLevel);
        if (isoCmp != 0) return isoCmp;

        return string.CompareOrdinal(left.FileName, right.FileName);
    }

    public void LoadPhotonuclearData(IEnumerable<Element> elements, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return;

        var isotopeMap = new Dictionary<(int Z, int A), Isotope>();
        foreach (var el in elements)
        {
            foreach (var iso in el.Isotopes)
            {
                isotopeMap[(iso.Z, iso.A)] = iso;
            }
        }

        if (isotopeMap.Count == 0) return;

        if (File.Exists(rootPath))
        {
            LoadUnifiedCsv(rootPath, isotopeMap);
            return;
        }

        if (!Directory.Exists(rootPath)) return;

        string unifiedPath = Path.Combine(rootPath, UnifiedFileName);
        if (File.Exists(unifiedPath))
        {
            LoadUnifiedCsv(unifiedPath, isotopeMap);
            return;
        }

        LoadLegacyDirectory(rootPath, isotopeMap);
    }

    private static void LoadUnifiedCsv(string filePath, Dictionary<(int Z, int A), Isotope> isotopeMap)
    {
        // Schema: Z,A,IsomerLevel,MT,Energy,CrossSection
        var parsedCurves = new Dictionary<(int Z, int A, int IsomerLevel), (List<double> E, List<double> Xs)>();

        foreach (string line in File.ReadLines(filePath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length < 6) continue;

            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) &&
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int isomerLevel) &&
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double energy) &&
                double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double crossSection))
            {
                if (!isotopeMap.ContainsKey((z, a))) continue;

                var curveKey = (z, a, isomerLevel);
                if (!parsedCurves.TryGetValue(curveKey, out var lists))
                {
                    lists = (new List<double>(), new List<double>());
                    parsedCurves[curveKey] = lists;
                }

                lists.E.Add(energy);
                lists.Xs.Add(crossSection);
            }
        }

        var bestByZa = new Dictionary<(int Z, int A), ((int Rank, int IsomerLevel, string FileName) Priority, (List<double> E, List<double> Xs) Curve)>();
        foreach (var kvp in parsedCurves)
        {
            var zaKey = (kvp.Key.Z, kvp.Key.A);
            var priority = GetSelectionPriority(kvp.Key.Z, kvp.Key.A, kvp.Key.IsomerLevel, string.Empty);
            if (!bestByZa.TryGetValue(zaKey, out var current) ||
                ComparePriority(priority, current.Priority) < 0)
            {
                bestByZa[zaKey] = (priority, kvp.Value);
            }
        }

        foreach (var kvp in bestByZa)
        {
            // C=0, S=0, X=0 identifies photonuclear curves
            isotopeMap[kvp.Key].PhotonuclearData = new CrossSectionCurve(0, 0, 0, kvp.Value.Curve.E.ToArray(), kvp.Value.Curve.Xs.ToArray());
        }
    }

    private void LoadLegacyDirectory(string rootPath, Dictionary<(int Z, int A), Isotope> isotopeMap)
    {
        var selectedFiles = new Dictionary<(int Z, int A), ((int Rank, int IsomerLevel, string FileName) Priority, string FilePath)>();
        foreach (var file in Directory.GetFiles(rootPath, "*.csv"))
        {
            string fileName = Path.GetFileName(file);
            if (!TryParseNuclideFileName(fileName, out int z, out int a, out int isomerLevel))
                continue;

            var key = (z, a);
            if (!isotopeMap.ContainsKey(key))
                continue;

            var priority = GetSelectionPriority(z, a, isomerLevel, fileName);
            if (!selectedFiles.TryGetValue(key, out var current) ||
                ComparePriority(priority, current.Priority) < 0)
            {
                selectedFiles[key] = (priority, file);
            }
        }

        foreach (var kvp in selectedFiles)
        {
            var (energies, values) = ReadCsvData(kvp.Value.FilePath);
            // C=0, S=0, X=0 identifies photonuclear curves
            isotopeMap[kvp.Key].PhotonuclearData = new CrossSectionCurve(0, 0, 0, energies, values);
        }
    }

    private (double[], double[]) ReadCsvData(string filePath)
    {
        var eList = new List<double>();
        var vList = new List<double>();

        foreach (var line in File.ReadAllLines(filePath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');

            if (parts.Length >= 2 &&
                double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double e) &&
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double xs))
            {
                eList.Add(e);
                vList.Add(xs);
            }
        }
        return (eList.ToArray(), vList.ToArray());
    }
}