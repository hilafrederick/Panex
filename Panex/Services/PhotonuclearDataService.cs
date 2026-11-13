// File: \Services\PhotonuclearDataService.cs
using Panex.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Panex.Services;

public class PhotonuclearDataService
{
    // Regex for ZA001002.csv (Z=001, A=002)
    private static readonly Regex FilePattern = new Regex(@"ZA(\d{3})(\d{3})\.csv", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void LoadPhotonuclearData(IEnumerable<Element> elements, string rootPath)
    {
        if (!Directory.Exists(rootPath)) return;

        // Map for quick lookup: Key = "Z-A" (e.g., "1-2"), Value = Isotope
        var isotopeMap = new Dictionary<string, Isotope>();
        foreach (var el in elements)
        {
            foreach (var iso in el.Isotopes)
            {
                isotopeMap[$"{iso.Z}-{iso.A}"] = iso;
            }
        }

        foreach (var file in Directory.GetFiles(rootPath, "*.csv"))
        {
            string fileName = Path.GetFileName(file);
            var match = FilePattern.Match(fileName);

            if (match.Success)
            {
                int z = int.Parse(match.Groups[1].Value);
                int a = int.Parse(match.Groups[2].Value);

                if (isotopeMap.TryGetValue($"{z}-{a}", out Isotope? iso))
                {
                    var (energies, values) = ReadCsvData(file);
                    // C=0, S=0, X=0 (Special ID for Photonuclear)
                    iso.PhotonuclearData = new CrossSectionCurve(0, 0, 0, energies, values);
                }
            }
        }
    }

    private (double[], double[]) ReadCsvData(string filePath)
    {
        var eList = new List<double>();
        var vList = new List<double>();

        // Skip header line
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