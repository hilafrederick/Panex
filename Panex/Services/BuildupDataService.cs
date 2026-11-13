using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panex.Models;

namespace Panex.Services;

public class BuildupDataService
{
    public void LoadParameters(IEnumerable<Element> elements, string ebfCsvPath, string eabfCsvPath)
    {
        var elementDict = elements.ToDictionary(e => e.Z);

        LoadFile(ebfCsvPath, elementDict, BuildupFactorType.Exposure);
        LoadFile(eabfCsvPath, elementDict, BuildupFactorType.EnergyAbsorption);
    }

    private void LoadFile(string path, Dictionary<int, Element> elements, BuildupFactorType type)
    {
        if (!File.Exists(path)) return;

        // Assumes CSV format: Z, Energy_MeV, a, b, c, d, Xk
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');

            int z = int.Parse(parts[0]);
            if (elements.TryGetValue(z, out Element? element))
            {
                var gp = new GpParameters
                {
                    EnergyMeV = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    A = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    B = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                    C = double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                    D = double.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                    Xk = double.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture)
                };

                if (type == BuildupFactorType.Exposure)
                    element.EbfParameters[gp.EnergyMeV] = gp;
                else
                    element.EabfParameters[gp.EnergyMeV] = gp;
            }
        }
    }
}