using Panex.Models;
using Panex.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Panex.Services;

public class LibraryMetadataDto
{
    public string DocumentCode { get; set; } = string.Empty;
    public string Citation { get; set; } = string.Empty;
}

public class MaterialLibraryDto
{
    public LibraryMetadataDto? Metadata { get; set; }
    public List<MaterialDto> Materials { get; set; } = new();
}

public class MaterialDto
{
    public string Name { get; set; } = string.Empty;
    public string Composition { get; set; } = string.Empty;
    public string FractionType { get; set; } = "Atom";
    public string Category { get; set; } = "Uncategorized";
    public double? Density { get; set; }

    [JsonPropertyName("Chemical Formula")]
    public string ChemicalFormula { get; set; } = string.Empty;
}

public partial class PeriodicTableService
{
    private Dictionary<int, Element> _elementsByZ = new();
    private Dictionary<string, Element> _elementsBySymbol = new();

    private readonly PhotoatomicDataService _epdlService;
    private readonly XcomDataService _xcomService;

    private PhotonuclearDataService _pnService = new();
    private BuildupDataService _buildupService = new();

    public string CurrentLibrary { get; private set; } = "EPDL";

    public PeriodicTableService(PhotoatomicDataService epdlService, XcomDataService xcomService)
    {
        _epdlService = epdlService;
        _xcomService = xcomService;
    }

    [GeneratedRegex(@"^([A-Za-z0-9\.]+)\s+([0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)$", RegexOptions.Compiled)]
    private static partial Regex MixtureLineRegex();

    [GeneratedRegex(@"([A-Z][a-z]*)(\d*\.?\d*)", RegexOptions.Compiled)]
    private static partial Regex ElementCountRegex();

    public IEnumerable<Element> GetAllElements() => _elementsByZ.Values.OrderBy(e => e.Z);

    public void LoadData(string isotopesPath, string elementsPath)
    {
        if (File.Exists(elementsPath))
        {
            foreach (var line in File.ReadAllLines(elementsPath).Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length < 4) continue;

                var e = new Element
                {
                    Z = int.Parse(parts[0]),
                    Name = parts[1],
                    Symbol = parts[2],
                    AtomicMass = double.Parse(parts[3])
                };
                _elementsByZ[e.Z] = e;
                _elementsBySymbol[e.Symbol] = e;
            }
        }

        if (File.Exists(isotopesPath))
        {
            foreach (var line in File.ReadAllLines(isotopesPath).Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length < 3) continue;

                int za = int.Parse(parts[0]);
                int z = za / 1000;
                int a = za % 1000;

                if (_elementsByZ.TryGetValue(z, out var element))
                {
                    element.Isotopes.Add(new Isotope
                    {
                        Z = z,
                        A = a,
                        Abundance = double.Parse(parts[1]),
                        Mass = double.Parse(parts[2])
                    });
                }
            }

            foreach (var element in _elementsByZ.Values)
            {
                element.RecalculateAtomicMass();
            }
        }

        string appBase = AppDomain.CurrentDomain.BaseDirectory;

        SwitchPhotoatomicLibrary("EPDL");

        // Photonuclear (JENDL-5)
        string pnPath = Path.Combine(appBase, "Data", "PN_JENDL5");
        if (Directory.Exists(pnPath))
            _pnService.LoadPhotonuclearData(_elementsByZ.Values, pnPath);

        // Buildup factors (ANSI/ANS-6.4.3)
        string ebfPath = Path.Combine(appBase, "Data", "ANSI_EBF.csv");
        string eabfPath = Path.Combine(appBase, "Data", "ANSI_EABF.csv");
        _buildupService.LoadParameters(_elementsByZ.Values, ebfPath, eabfPath);
    }

    /// <summary>
    /// Swaps the underlying photoatomic data (EPDL vs XCOM) and regenerates absorption edges.
    /// </summary>
    public void SwitchPhotoatomicLibrary(string libraryType)
    {
        CurrentLibrary = libraryType;
        string appBase = AppDomain.CurrentDomain.BaseDirectory;

        foreach (var el in _elementsByZ.Values)
        {
            el.PhotoatomicData = new List<CrossSectionCurve>();
            el.AbsorptionEdges.Clear();
            el.ClearPhotoatomicCache();
        }

        if (libraryType == "XCOM")
        {
            string paPath = Path.Combine(appBase, "Data", "PA_NIST_XCOM");
            if (Directory.Exists(paPath)) _xcomService.LoadCrossSections(_elementsByZ.Values, paPath);
        }
        else
        {
            string paPath = Path.Combine(appBase, "Data", "PA_EPDL_2025");
            if (Directory.Exists(paPath)) _epdlService.LoadCrossSections(_elementsByZ.Values, paPath);

            // EPDL stores absorption edges in a separate CSV
            string edgesPath = Path.Combine(appBase, "Data", "PA_EPDL_2025", "epdl_2025_absorption_edges.csv");
            if (File.Exists(edgesPath))
            {
                foreach (var line in File.ReadAllLines(edgesPath).Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 3 && int.TryParse(parts[0], out int z) && _elementsByZ.TryGetValue(z, out var element))
                    {
                        element.AbsorptionEdges.Add(new AbsorptionEdgeRow
                        {
                            Element = element.Symbol,
                            Subshell = parts[1],
                            EnergyMeV = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)
                        });
                    }
                }
            }
        }
    }

    public IEnumerable<Material> LoadMaterialsFromJson(string jsonPath)
    {
        var library = new List<Material>();
        if (!File.Exists(jsonPath)) return library;

        string fallbackReference = Path.GetFileNameWithoutExtension(jsonPath);
        try
        {
            string json = File.ReadAllText(jsonPath);
            MaterialLibraryDto? libraryDto = null;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            try
            {
                libraryDto = JsonSerializer.Deserialize<MaterialLibraryDto>(json, options);
            }
            catch
            {
                var rawArray = JsonSerializer.Deserialize<List<MaterialDto>>(json, options);
                if (rawArray != null)
                {
                    libraryDto = new MaterialLibraryDto { Materials = rawArray };
                }
            }

            if (libraryDto?.Materials == null) return library;

            string refName = !string.IsNullOrWhiteSpace(libraryDto.Metadata?.DocumentCode)
                ? libraryDto.Metadata.DocumentCode
                : fallbackReference;
            string fullCitation = libraryDto.Metadata?.Citation ?? "";

            foreach (var dto in libraryDto.Materials)
            {
                try
                {
                    bool isWeight = dto.FractionType.Equals("Weight", StringComparison.OrdinalIgnoreCase);
                    string mixtureInput = dto.Composition.Replace(";", "\n");

                    var material = CreateFromMixture(dto.Name, mixtureInput, dto.Density, isWeight);

                    material.Category = !string.IsNullOrWhiteSpace(dto.Category) ? dto.Category : "Uncategorized";
                    material.Reference = refName;
                    material.Citation = fullCitation;

                    library.Add(material);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load '{dto.Name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read JSON file '{jsonPath}': {ex.Message}");
        }

        return library;
    }

    public Material CreateFromElement(string name, Element element, double? density)
    {
        return new Material
        {
            Name = name,
            Density = density,
            Composition = new Dictionary<Element, double> { { element, 1.0 } }
        };
    }

    public Material CreateFromCompound(string name, string formula, double? density)
    {
        if (string.IsNullOrWhiteSpace(formula))
            throw new ArgumentException("Formula cannot be empty.");

        var composition = ParseFormula(formula);

        if (composition.Count == 0)
            throw new ArgumentException($"Invalid formula syntax: '{formula}'");

        double totalAtoms = composition.Values.Sum();
        composition = composition.ToDictionary(k => k.Key, v => v.Value / totalAtoms);

        return new Material
        {
            Name = name,
            Density = density,
            Composition = composition
        };
    }

    public Material CreateFromMixture(string name, string mixtureInput, double? density, bool isWeight)
    {
        var lines = mixtureInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var elementMoles = new Dictionary<Element, double>();

        if (lines.Length == 0) throw new ArgumentException("Mixture cannot be empty.");

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var match = MixtureLineRegex().Match(trimmed);
            if (!match.Success)
                throw new FormatException($"Invalid syntax in line: '{trimmed}'. Expected 'Formula Value'");

            string formula = match.Groups[1].Value;
            double value = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

            var formulaComposition = ParseFormula(formula);
            if (formulaComposition.Count == 0)
                throw new ArgumentException($"Unknown formula in mixture: '{formula}'");

            double molesOfComponent;
            if (isWeight)
            {
                double molarMass = formulaComposition.Sum(x => x.Key.AtomicMass * x.Value);
                if (molarMass <= 0) throw new ArgumentException($"Invalid molar mass for {formula}");
                molesOfComponent = value / molarMass;
            }
            else
            {
                molesOfComponent = value;
            }

            foreach (var kvp in formulaComposition)
            {
                if (!elementMoles.ContainsKey(kvp.Key)) elementMoles[kvp.Key] = 0;
                elementMoles[kvp.Key] += molesOfComponent * kvp.Value;
            }
        }

        double totalMoles = elementMoles.Values.Sum();
        var finalComposition = new Dictionary<Element, double>();

        if (totalMoles > 0)
        {
            foreach (var kvp in elementMoles)
                finalComposition[kvp.Key] = kvp.Value / totalMoles;
        }

        return new Material
        {
            Name = name,
            Density = density,
            Composition = finalComposition
        };
    }

    public IEnumerable<Material> LoadMaterialLibrary(string jsonPath)
    {
        var library = new List<Material>();
        if (!File.Exists(jsonPath)) return library;

        string sourceReference = System.IO.Path.GetFileNameWithoutExtension(jsonPath);

        try
        {
            string json = File.ReadAllText(jsonPath);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<MaterialDto>>(json);
            if (dtos == null) return library;

            foreach (var dto in dtos)
            {
                try
                {
                    bool isWeight = dto.FractionType.Equals("Weight", StringComparison.OrdinalIgnoreCase);
                    string mixtureInput = dto.Composition.Replace(";", "\n");

                    var material = CreateFromMixture(dto.Name, mixtureInput, dto.Density, isWeight);

                    material.Category = !string.IsNullOrWhiteSpace(dto.Category) ? dto.Category : "Uncategorized";
                    material.Reference = sourceReference;

                    library.Add(material);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load '{dto.Name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read JSON file '{jsonPath}': {ex.Message}");
        }

        return library;
    }

    public void SaveUserMaterial(Material material, string jsonPath)
    {
        var currentLibrary = LoadMaterialLibrary(jsonPath).ToList();
        currentLibrary.Add(material);
        OverwriteUserMaterials(currentLibrary, jsonPath);
    }

    public void OverwriteUserMaterials(IEnumerable<Material> materials, string jsonPath)
    {
        string? directory = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dtos = materials.Select(m => new MaterialDto
        {
            Name = m.Name,
            Density = m.Density,
            FractionType = "Atom",
            Composition = string.Join("; ", m.Composition.Select(x => $"{x.Key.Symbol} {x.Value}"))
        }).ToList();

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(dtos, options);
        File.WriteAllText(jsonPath, json);
    }

    public void SaveMaterialsToJson(IEnumerable<Material> materials, string jsonPath)
    {
        var dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var libraryDto = new MaterialLibraryDto
        {
            Metadata = new LibraryMetadataDto
            {
                DocumentCode = "User Materials",
                Citation = "Custom materials defined by the user during Panex sessions."
            },
            Materials = materials.Select(m => new MaterialDto
            {
                Name = m.Name,
                Category = m.Category,
                Density = m.Density,
                FractionType = "Atom",
                Composition = string.Join("; ", m.Composition.Select(kvp => $"{kvp.Key.Symbol} {kvp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
                ChemicalFormula = ""
            }).ToList()
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(libraryDto, options);
        File.WriteAllText(jsonPath, json);
    }

    private Dictionary<Element, double> ParseFormula(string formula)
    {
        var result = new Dictionary<Element, double>();
        string cleanFormula = formula.Replace("\t", "");

        var matches = ElementCountRegex().Matches(cleanFormula);

        int matchedLength = 0;
        foreach (Match m in matches) matchedLength += m.Length;
        if (matchedLength != cleanFormula.Length)
        {
            throw new FormatException($"Formula contains invalid characters or format: '{formula}'");
        }

        foreach (Match m in matches)
        {
            string symbol = m.Groups[1].Value;
            string countStr = m.Groups[2].Value;

            double count = string.IsNullOrEmpty(countStr) ? 1.0 : double.Parse(countStr, System.Globalization.CultureInfo.InvariantCulture);

            if (_elementsBySymbol.TryGetValue(symbol, out var element))
            {
                if (!result.ContainsKey(element)) result[element] = 0;
                result[element] += count;
            }
            else
            {
                throw new ArgumentException($"Unknown element symbol: '{symbol}'");
            }
        }

        return result;
    }

    public double CalculateInterpolatedZeff(double energyMeV, double materialAtomicCrossSection, int minZ, int maxZ, double directZeff)
    {
        if (minZ >= maxZ) return minZ;

        var results = new List<double>();
        double[] s = new double[maxZ + 1];

        for (int z = minZ; z <= maxZ; z++)
        {
            if (_elementsByZ.TryGetValue(z, out var el))
            {
                s[z] = el.GetTotalCrossSection(energyMeV);
            }
        }

        for (int z = minZ; z < maxZ; z++)
        {
            double s1 = s[z];
            double s2 = s[z + 1];
            double st = materialAtomicCrossSection;

            if (st == s1 && !results.Contains(z)) results.Add(z);

            if ((s2 > st && st > s1) || (s1 > st && st > s2))
            {
                if (s1 > 0 && s2 > 0 && st > 0)
                {
                    double zeff = PhysicsConstants.InterpolateLinearOnLogX(st, s1, s2, z, z + 1);
                    results.Add(zeff);
                }
            }
        }

        if (results.Count == 0) return 0.0;
        if (results.Count == 1) return results[0];

        return results.OrderBy(v => Math.Abs(v - directZeff)).First();
    }
}