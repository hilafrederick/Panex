using Panex.Models;
using Panex.Services;
using System.Globalization;
using Xunit;

namespace Panex.Tests;

public class PhotonuclearDataServiceTests
{
    [Theory]
    [InlineData("ZA001002.csv", true, 1, 2, 0)]
    [InlineData("ZA073180m.csv", true, 73, 180, 1)]
    [InlineData("za073180m1.csv", true, 73, 180, 1)]
    [InlineData("ZA095242m2.csv", true, 95, 242, 2)]
    [InlineData("backup_ZA001002.csv", false, 0, 0, 0)]
    [InlineData("ZA073180m.txt", false, 0, 0, 0)]
    [InlineData("notes.csv", false, 0, 0, 0)]
    [InlineData("ZA073180m99999999999.csv", false, 0, 0, 0)]
    public void TryParseNuclideFileName_HandlesGroundStateIsomersAndInvalidNames(
        string fileName, bool expectedOk, int expectedZ, int expectedA, int expectedIso)
    {
        bool ok = PhotonuclearDataService.TryParseNuclideFileName(fileName, out int z, out int a, out int iso);
        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedZ, z);
        Assert.Equal(expectedA, a);
        Assert.Equal(expectedIso, iso);
    }

    [Fact]
    public void LoadPhotonuclearData_PrefersTa180mOverGroundStateWhenBothExist()
    {
        using var tmp = new TempDir();
        WriteSimpleCsv(Path.Combine(tmp.Path, "ZA073180.csv"), (1.0, 0.1), (15.5, 0.2));
        WriteSimpleCsv(Path.Combine(tmp.Path, "ZA073180m.csv"), (1.0, 0.3), (15.5, 0.36));

        var ta = new Element
        {
            Z = 73,
            Symbol = "Ta",
            Name = "Tantalum",
            Isotopes = { new Isotope { Z = 73, A = 180, Abundance = 0.0001201, Mass = 179.9474676 } }
        };

        new PhotonuclearDataService().LoadPhotonuclearData(new[] { ta }, tmp.Path);

        Assert.NotNull(ta.Isotopes[0].PhotonuclearData);
        Assert.Equal(0.36, ta.Isotopes[0].GetPhotonuclearCrossSection(15.5), 12);
    }

    [Fact]
    public void LoadPhotonuclearData_PrefersGroundStateOverIsomerForNonTa180AndFallsBackWhenGsMissing()
    {
        using var tmpBoth = new TempDir();
        WriteSimpleCsv(Path.Combine(tmpBoth.Path, "ZA047110.csv"), (1.0, 0.5), (15.0, 0.7));
        WriteSimpleCsv(Path.Combine(tmpBoth.Path, "ZA047110m.csv"), (1.0, 0.9), (15.0, 1.1));

        var agBoth = new Element
        {
            Z = 47,
            Symbol = "Ag",
            Name = "Silver",
            Isotopes = { new Isotope { Z = 47, A = 110, Abundance = 1.0, Mass = 109.9 } }
        };
        new PhotonuclearDataService().LoadPhotonuclearData(new[] { agBoth }, tmpBoth.Path);
        Assert.NotNull(agBoth.Isotopes[0].PhotonuclearData);
        Assert.Equal(0.7, agBoth.Isotopes[0].GetPhotonuclearCrossSection(15.0), 12);

        using var tmpIsoOnly = new TempDir();
        WriteSimpleCsv(Path.Combine(tmpIsoOnly.Path, "ZA047110m1.csv"), (1.0, 0.9), (15.0, 1.1));

        var agFallback = new Element
        {
            Z = 47,
            Symbol = "Ag",
            Name = "Silver",
            Isotopes = { new Isotope { Z = 47, A = 110, Abundance = 1.0, Mass = 109.9 } }
        };
        new PhotonuclearDataService().LoadPhotonuclearData(new[] { agFallback }, tmpIsoOnly.Path);
        Assert.NotNull(agFallback.Isotopes[0].PhotonuclearData);
        Assert.Equal(1.1, agFallback.Isotopes[0].GetPhotonuclearCrossSection(15.0), 12);
    }

    [Fact]
    public void LoadPhotonuclearData_UnifiedCsvPrefersTa180mAndGroundStateForOthers()
    {
        using var tmp = new TempDir();
        string unified = Path.Combine(tmp.Path, PhotonuclearDataService.UnifiedFileName);
        File.WriteAllLines(unified, new[]
        {
            "Z,A,IsomerLevel,MT,Energy,CrossSection",
            "73,180,0,3,1.0,0.1",
            "73,180,0,3,15.5,0.2",
            "73,180,1,3,1.0,0.3",
            "73,180,1,3,15.5,0.36",
            "47,110,0,5,1.0,0.5",
            "47,110,0,5,15.0,0.7",
            "47,110,1,5,1.0,0.9",
            "47,110,1,5,15.0,1.1"
        });

        var elements = new[]
        {
            new Element
            {
                Z = 73,
                Symbol = "Ta",
                Name = "Tantalum",
                Isotopes = { new Isotope { Z = 73, A = 180, Abundance = 0.0001201, Mass = 179.9474676 } }
            },
            new Element
            {
                Z = 47,
                Symbol = "Ag",
                Name = "Silver",
                Isotopes = { new Isotope { Z = 47, A = 110, Abundance = 1.0, Mass = 109.9 } }
            }
        };

        new PhotonuclearDataService().LoadPhotonuclearData(elements, unified);

        Assert.Equal(0.36, elements[0].Isotopes[0].GetPhotonuclearCrossSection(15.5), 12);
        Assert.Equal(0.7, elements[1].Isotopes[0].GetPhotonuclearCrossSection(15.0), 12);
    }

    [Fact]
    public void LoadPhotonuclearData_BundledFolderContainsOnlyUnifiedCsvAndMatchesLegacyLayoutWithZeroDiff()
    {
        string dataDir = FindPanexDataDirectory();
        string elementsCsv = Path.Combine(dataDir, "Elements.csv");
        string isotopesCsv = Path.Combine(dataDir, "Isotopes.csv");
        string pnDir = Path.Combine(dataDir, "PN_JENDL5");
        string unifiedCsv = Path.Combine(pnDir, PhotonuclearDataService.UnifiedFileName);

        string[] csvFilesInPnDir = Directory.GetFiles(pnDir, "*.csv").Select(Path.GetFileName).ToArray()!;
        Assert.Equal(new[] { PhotonuclearDataService.UnifiedFileName }, csvFilesInPnDir);

        // Materialize legacy ZA*.csv files in an isolated temp folder from unified rows to exercise LoadLegacyDirectory parity
        using var legacyTmp = new TempDir();
        var groupedRows = new Dictionary<(int Z, int A, int Iso, int Mt), List<string>>();
        foreach (string line in File.ReadLines(unifiedCsv).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] p = line.Split(',');
            var key = (
                int.Parse(p[0], CultureInfo.InvariantCulture),
                int.Parse(p[1], CultureInfo.InvariantCulture),
                int.Parse(p[2], CultureInfo.InvariantCulture),
                int.Parse(p[3], CultureInfo.InvariantCulture));
            if (!groupedRows.TryGetValue(key, out var list))
            {
                list = new List<string>();
                groupedRows[key] = list;
            }
            list.Add($"{p[4]},{p[5]}");
        }
        Assert.Equal(286, groupedRows.Count);

        foreach (var kvp in groupedRows)
        {
            string suffix = kvp.Key.Iso == 0 ? "" : (kvp.Key.Iso == 1 ? "m" : $"m{kvp.Key.Iso}");
            string name = $"ZA{kvp.Key.Z:D3}{kvp.Key.A:D3}{suffix}.csv";
            using var sw = new StreamWriter(Path.Combine(legacyTmp.Path, name));
            sw.WriteLine($"Energy_MeV,Photoabsorption_cross_section_MT{kvp.Key.Mt}");
            foreach (string row in kvp.Value) sw.WriteLine(row);
        }

        Dictionary<int, Element> unifiedEls = LoadElementsAndIsotopes(elementsCsv, isotopesCsv);
        Dictionary<int, Element> legacyEls = LoadElementsAndIsotopes(elementsCsv, isotopesCsv);

        new PhotonuclearDataService().LoadPhotonuclearData(unifiedEls.Values, unifiedCsv);
        new PhotonuclearDataService().LoadPhotonuclearData(legacyEls.Values, legacyTmp.Path);

        for (int z = 1; z <= 100; z++)
        {
            Element uEl = unifiedEls[z];
            Element lEl = legacyEls[z];

            foreach (Isotope lIso in lEl.Isotopes)
            {
                Isotope uIso = uEl.Isotopes.Single(i => i.A == lIso.A);
                if (lIso.PhotonuclearData == null)
                {
                    Assert.Null(uIso.PhotonuclearData);
                    continue;
                }

                Assert.NotNull(uIso.PhotonuclearData);
                Assert.Equal(lIso.PhotonuclearData.Energies, uIso.PhotonuclearData!.Energies);
                Assert.Equal(lIso.PhotonuclearData.CrossSections, uIso.PhotonuclearData.CrossSections);
            }

            CrossSectionCurve uCurve = uEl.GetElementalPhotonuclearCurve();
            CrossSectionCurve lCurve = lEl.GetElementalPhotonuclearCurve();
            Assert.Equal(lCurve.Energies, uCurve.Energies);
            Assert.Equal(lCurve.CrossSections, uCurve.CrossSections);

            if (lCurve.Energies.Length == 0) continue;

            var evalGrid = new SortedSet<double>(lCurve.Energies);
            for (int step = 5; step <= 2000; step++) evalGrid.Add(step * 0.1);

            foreach (double e in evalGrid)
            {
                Assert.Equal(0.0, Math.Abs(uCurve.Interpolate(e) - lCurve.Interpolate(e)));
            }
        }
    }

    [Fact]
    public void LoadPhotonuclearData_LoadsAll286BundledNuclidesAndMatchesElementalReferenceZ1To100()
    {
        string dataDir = FindPanexDataDirectory();
        string elementsCsv = Path.Combine(dataDir, "Elements.csv");
        string isotopesCsv = Path.Combine(dataDir, "Isotopes.csv");
        string pnDir = Path.Combine(dataDir, "PN_JENDL5");
        string unifiedCsv = Path.Combine(pnDir, PhotonuclearDataService.UnifiedFileName);

        Assert.True(File.Exists(unifiedCsv), $"Missing unified CSV: {unifiedCsv}");

        Dictionary<int, Element> elements = LoadElementsAndIsotopes(elementsCsv, isotopesCsv);
        Assert.Equal(100, elements.Count);

        new PhotonuclearDataService().LoadPhotonuclearData(elements.Values, pnDir);

        var loadedIsotopes = elements.Values
            .SelectMany(e => e.Isotopes)
            .Where(i => i.PhotonuclearData != null && i.PhotonuclearData.Energies.Length > 0)
            .ToList();
        Assert.Equal(286, loadedIsotopes.Count);

        var missingNatural = elements.Values
            .SelectMany(e => e.Isotopes)
            .Where(i => i.PhotonuclearData == null)
            .Select(i => (i.Z, i.A))
            .OrderBy(k => k)
            .ToList();
        Assert.Equal(new[] { (1, 1), (2, 4) }, missingNatural);

        // Verify Ta-180m (IsomerLevel=1) loaded cleanly into ZA=73180
        Isotope ta180 = elements[73].Isotopes.Single(i => i.A == 180);
        Assert.NotNull(ta180.PhotonuclearData);
        Assert.Equal(90, ta180.PhotonuclearData!.Energies.Length);
        Assert.Equal(1.0, ta180.PhotonuclearData.Energies[0], 12);
        Assert.Equal(200.0, ta180.PhotonuclearData.Energies[^1], 12);
        Assert.Equal(0.3601509, ta180.GetPhotonuclearCrossSection(15.5), 12);

        // Verify H-2 INT=3 linearized curve remains intact (237 pts)
        Isotope h2 = elements[1].Isotopes.Single(i => i.A == 2);
        Assert.NotNull(h2.PhotonuclearData);
        Assert.Equal(237, h2.PhotonuclearData!.Energies.Length);
        Assert.Equal(2.224, h2.PhotonuclearData.Energies[0], 12);
        Assert.Equal(140.0, h2.PhotonuclearData.Energies[^1], 12);

        // Verify every element Z=1..100 across 0.5-200 MeV against independent unified-CSV abundance-weighted sum
        var rawCurves = LoadUnifiedCurvesByZa(unifiedCsv);
        Assert.Equal(286, rawCurves.Count);
        for (int z = 1; z <= 100; z++)
        {
            Element el = elements[z];
            CrossSectionCurve curve = el.GetElementalPhotonuclearCurve();

            if (el.Isotopes.All(i => !rawCurves.ContainsKey((i.Z, i.A))))
            {
                Assert.Empty(curve.Energies);
                Assert.Equal(0.0, curve.Interpolate(15.0), 15);
                continue;
            }

            var evalGrid = new SortedSet<double>(curve.Energies);
            for (int step = 5; step <= 2000; step++)
            {
                evalGrid.Add(step * 0.1);
            }

            foreach (double e in evalGrid)
            {
                double expected = 0.0;
                foreach (var iso in el.Isotopes)
                {
                    if (rawCurves.TryGetValue((iso.Z, iso.A), out var raw))
                    {
                        expected += iso.Abundance * InterpolatePanexLinear(raw.Energies, raw.Values, e);
                    }
                }

                double actual = curve.Interpolate(e);
                double tol = Math.Max(1e-14, expected * 1e-12);
                Assert.True(
                    Math.Abs(actual - expected) <= tol,
                    $"Element Z={z} ({el.Symbol}) at {e} MeV: actual={actual}, expected={expected}");
            }
        }
    }

    private static string FindPanexDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Panex", "Data");
            if (File.Exists(Path.Combine(candidate, "Isotopes.csv")) &&
                Directory.Exists(Path.Combine(candidate, "PN_JENDL5")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Panex/Data/PN_JENDL5 from test base directory.");
    }

    private static Dictionary<int, Element> LoadElementsAndIsotopes(string elementsCsv, string isotopesCsv)
    {
        var byZ = new Dictionary<int, Element>();
        foreach (string line in File.ReadAllLines(elementsCsv).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            int z = int.Parse(parts[0], CultureInfo.InvariantCulture);
            byZ[z] = new Element
            {
                Z = z,
                Name = parts[1],
                Symbol = parts[2],
                AtomicMass = double.Parse(parts[3], CultureInfo.InvariantCulture)
            };
        }

        foreach (string line in File.ReadAllLines(isotopesCsv).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            int za = int.Parse(parts[0], CultureInfo.InvariantCulture);
            int z = za / 1000;
            int a = za % 1000;
            if (byZ.TryGetValue(z, out Element? el))
            {
                el.Isotopes.Add(new Isotope
                {
                    Z = z,
                    A = a,
                    Abundance = double.Parse(parts[1], CultureInfo.InvariantCulture),
                    Mass = double.Parse(parts[2], CultureInfo.InvariantCulture)
                });
            }
        }

        return byZ;
    }

    private static Dictionary<(int Z, int A), (double[] Energies, double[] Values)> LoadUnifiedCurvesByZa(string unifiedCsvPath)
    {
        var grouped = new Dictionary<(int Z, int A), (List<double> Energies, List<double> Values)>();
        foreach (string line in File.ReadLines(unifiedCsvPath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            int z = int.Parse(parts[0], CultureInfo.InvariantCulture);
            int a = int.Parse(parts[1], CultureInfo.InvariantCulture);
            double e = double.Parse(parts[4], CultureInfo.InvariantCulture);
            double xs = double.Parse(parts[5], CultureInfo.InvariantCulture);

            if (!grouped.TryGetValue((z, a), out var list))
            {
                list = (new List<double>(), new List<double>());
                grouped[(z, a)] = list;
            }
            list.Energies.Add(e);
            list.Values.Add(xs);
        }

        return grouped.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.Energies.ToArray(), kvp.Value.Values.ToArray()));
    }

    private static double InterpolatePanexLinear(double[] x, double[] y, double query)
    {
        if (x.Length == 0 || query < x[0]) return 0.0;
        if (query >= x[^1]) return y[^1];
        int idx = Array.BinarySearch(x, query);
        if (idx >= 0) return y[idx];
        int right = ~idx;
        int left = right - 1;
        double frac = (query - x[left]) / (x[right] - x[left]);
        return y[left] + frac * (y[right] - y[left]);
    }

    private static void WriteSimpleCsv(string path, params (double E, double Xs)[] points)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine("Energy_MeV,Photoabsorption_cross_section_MT3");
        foreach (var (e, xs) in points)
        {
            sw.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{e},{xs}"));
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "panex_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
