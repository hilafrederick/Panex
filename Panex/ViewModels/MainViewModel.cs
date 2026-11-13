using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Panex.Models;
using Panex.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Panex.ViewModels;

public partial class MainViewModel : ObservableObject
{
    #region Services & Child ViewModels
    private readonly PeriodicTableService _ptService;
    public BuildupCalculator BuildupCalc { get; }

    public MaterialLibraryViewModel LibraryVM { get; }
    public ChartViewModel ChartVM { get; }
    public DataTableViewModel TableVM { get; }

    public event EventHandler? RequestRepaint;
    public event Action<string, string>? ShowAlert;
    public event Func<string, string, string, Task<bool>>? RequestConfirmation;
    public event EventHandler? MaterialCreated;
    #endregion

    #region View State & UI Properties
    [ObservableProperty] private bool isLoadMode = false;
    [ObservableProperty] private int selectedLoadTabIndex = 0;
    [ObservableProperty] private int selectedInputTabIndex = 0;
    [ObservableProperty] private bool isLoading = false;
    [ObservableProperty] private bool showChart = true;
    [ObservableProperty] private bool showGrid = false;
    [ObservableProperty] private bool isMixtureMode = false;

    [ObservableProperty] private bool isCompareMode = false;

    public bool IsDarkMode
    {
        get => ChartVM.IsDarkMode;
        set { ChartVM.IsDarkMode = value; OnPropertyChanged(); }
    }

    partial void OnShowChartChanged(bool value)
    {
        if (value) { ShowGrid = false; RequestRepaint?.Invoke(this, EventArgs.Empty); }
    }
    partial void OnShowGridChanged(bool value)
    {
        if (value)
        {
            ShowChart = false;
            if (IsZeqMode) IsEbfMode = true;
            UpdateTables();
        }
    }

    partial void OnIsCompareModeChanged(bool value)
    {
        if (value)
        {
            foreach (var mat in Materials)
            {
                mat.IsCompared = true;
            }

            // Preserve multi-MFP input while Compare Mode narrows to a single target MFP
            _mfpMemory = TargetMfpInput;

            if (TargetMfps.Length > 1)
            {
                TargetMfpInput = TargetMfps.FirstOrDefault().ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_mfpMemory))
            {
                TargetMfpInput = _mfpMemory;
            }
        }

        OnPropertyChanged(nameof(ShowMainContent));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasValidDensity));
        OnPropertyChanged(nameof(HasBuildupFactors));

        ValidateCurrentTab();

        RefreshUIOnly();
    }
    #endregion

    #region Chart Settings States (Forwarded to ChartVM)
    [ObservableProperty] private PlotSettings currentPlotSettings;
    [ObservableProperty] private int selectedMainTabIndex = 0;
    [ObservableProperty] private bool isBuildupPlotActive = true;

    public PlotSettings CrossSectionSettings => ChartVM.CrossSectionSettings;
    public PlotSettings MacSettings => ChartVM.MacSettings;
    public PlotSettings LacSettings => ChartVM.LacSettings;
    public PlotSettings DepthsSettings => ChartVM.DepthsSettings;
    public PlotSettings ZeffSettings => ChartVM.ZeffSettings;
    public PlotSettings BuildupSettings => ChartVM.BuildupSettings;
    public PlotSettings ZeqSettings => ChartVM.ZeqSettings;
    public Dictionary<PlotSettings, List<HoverCurveCache>> HoverDataMap => ChartVM.HoverDataMap;

    partial void OnSelectedMainTabIndexChanged(int value)
    {
        CurrentPlotSettings = value switch
        {
            0 => CrossSectionSettings,
            1 => MacSettings,
            2 => LacSettings,
            3 => DepthsSettings,
            4 => ZeffSettings,
            5 => IsBuildupPlotActive ? BuildupSettings : ZeqSettings,
            _ => CrossSectionSettings
        };
    }

    partial void OnIsBuildupPlotActiveChanged(bool value)
    {
        if (SelectedMainTabIndex == 5) CurrentPlotSettings = value ? BuildupSettings : ZeqSettings;
        RequestRepaint?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    #region Physics Parameters & Toggles
    [ObservableProperty] private bool showTotal = true;
    [ObservableProperty] private bool showCoherent = true;
    [ObservableProperty] private bool showIncoherent = true;
    [ObservableProperty] private bool showPhotoelectric = true;
    [ObservableProperty] private bool showPair = true;
    [ObservableProperty] private bool showTriplet = true;
    [ObservableProperty] private bool showPhotonuclear = true;
    [ObservableProperty] private double nValue = 2.0;

    // Shared interaction filters for Depth calculations and Compare Mode
    [ObservableProperty] private bool depthIncludePhotoelectric = true;
    [ObservableProperty] private bool depthIncludeCoherent = true;
    [ObservableProperty] private bool depthIncludeIncoherent = true;
    [ObservableProperty] private bool depthIncludePair = true;
    [ObservableProperty] private bool depthIncludeTriplet = true;
    [ObservableProperty] private bool depthIncludePhotonuclear = true;

    [ObservableProperty] private bool compareUseInterpolatedZeff = false;
    partial void OnCompareUseInterpolatedZeffChanged(bool value) { RefreshUIOnly(); }

    public BuildupFactorType SelectedBuildupType { get; set; } = BuildupFactorType.Exposure;
    [ObservableProperty] private bool isEbfMode = true;
    [ObservableProperty] private bool isEabfMode = false;
    [ObservableProperty] private bool isZeqMode = false;

    [ObservableProperty] private bool isUserGrid = true;
    [ObservableProperty] private string userGridInput = "0.01, 0.05, 0.1, 0.5, 0.661657, 1.0, 1.25, 2.0, 5.0, 10.0";
    [ObservableProperty] private string targetMfpInput = "1, 2, 5, 10, 20, 30, 40";

    partial void OnNValueChanged(double value) { TableVM.ClearCaches(); RefreshUIOnly(); }

    partial void OnDepthIncludePhotoelectricChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnDepthIncludeCoherentChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnDepthIncludeIncoherentChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnDepthIncludePairChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnDepthIncludeTripletChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnDepthIncludePhotonuclearChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }

    partial void OnShowTotalChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnShowCoherentChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnShowIncoherentChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnShowPhotoelectricChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnShowPairChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnShowTripletChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }
    partial void OnShowPhotonuclearChanged(bool value) { TableVM.ClearCaches(); RefreshUIOnly(); }

    partial void OnIsEbfModeChanged(bool value) { if (value) { SelectedBuildupType = BuildupFactorType.Exposure; IsBuildupPlotActive = true; TableVM.ClearCaches(); RefreshUIOnly(); } }
    partial void OnIsEabfModeChanged(bool value) { if (value) { SelectedBuildupType = BuildupFactorType.EnergyAbsorption; IsBuildupPlotActive = true; TableVM.ClearCaches(); RefreshUIOnly(); } }
    partial void OnIsZeqModeChanged(bool value) { if (value) { IsBuildupPlotActive = false; TableVM.ClearCaches(); RefreshUIOnly(); } }

    partial void OnIsUserGridChanged(bool value) { TableVM.ClearCaches(); UpdateTables(); }
    partial void OnUserGridInputChanged(string value) { TableVM.ClearCaches(); UpdateTables(); }
    partial void OnTargetMfpInputChanged(string value)
    {
        OnPropertyChanged(nameof(TargetMfps));
        TableVM.ClearCaches();
        RefreshUIOnly();
    }

    public double[] TargetMfps
    {
        get
        {
            var parts = TargetMfpInput.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var mfps = new List<double>();
            foreach (var part in parts)
            {
                if (double.TryParse(part, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val) && val >= 0) mfps.Add(val);
            }
            return mfps.Distinct().OrderBy(x => x).ToArray();
        }
    }
    #endregion

    #region Composition Inputs & Library Forwarding
    [ObservableProperty] private string materialNameInput = "New Material";
    [ObservableProperty] private string materialDensityInput = "";
    [ObservableProperty] private Element? selectedElementInput;
    [ObservableProperty] private string elementSearchText = "";
    public bool IsCreateMode
    {
        get => !IsLoadMode;
        set { if (value) IsLoadMode = false; }
    }
    [ObservableProperty] private string compoundFormulaInput = "H2O";
    [ObservableProperty] private string mixtureInput = "H2O 0.9\r\nNaCl 0.1";
    [ObservableProperty] private bool isMixtureByWeight = true;

    private string[] _densityMemories = new string[3] { "", "", "" };
    private string _mfpMemory = "";

    public ObservableCollection<Material> Materials => LibraryVM.Materials;
    public bool HasMaterials => Materials.Count > 0;
    public IEnumerable<Material> ComparedMaterials => Materials.Where(m => m.IsCompared);

    public ObservableCollection<Material> MaterialLibrary => LibraryVM.MaterialLibrary;
    public ObservableCollection<Material> FilteredLibrary => LibraryVM.FilteredLibrary;
    public ObservableCollection<AbsorptionEdgeRow>? AbsorptionEdgesData => TableVM.AbsorptionEdgesData;
    public IRelayCommand CopyAbsorptionEdgesTableCommand => TableVM.CopyAbsorptionEdgesTableCommand;

    public Material? SelectedMaterial
    {
        get => LibraryVM.SelectedMaterial;
        set
        {
            LibraryVM.SelectedMaterial = value;
            OnPropertyChanged();

            OnPropertyChanged(nameof(ShowMainContent));
            OnPropertyChanged(nameof(ShowEmptyState));

            OnSelectedMaterialChanged(value);
        }
    }
    public Material? SelectedLibraryMaterial
    {
        get => LibraryVM.SelectedLibraryMaterial;
        set { LibraryVM.SelectedLibraryMaterial = value; OnPropertyChanged(); }
    }

    public string LibrarySearchText
    {
        get => LibraryVM.LibrarySearchText;
        set { LibraryVM.LibrarySearchText = value; OnPropertyChanged(); }
    }

    public bool ShowCustomMaterials
    {
        get => LibraryVM.ShowCustomMaterials;
        set { LibraryVM.ShowCustomMaterials = value; OnPropertyChanged(); }
    }

    public bool IsRenamingCustomMaterial
    {
        get => LibraryVM.IsRenamingCustomMaterial;
        set { LibraryVM.IsRenamingCustomMaterial = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotRenamingCustomMaterial)); }
    }
    public bool IsNotRenamingCustomMaterial => !IsRenamingCustomMaterial;

    public bool HasSelectedLibraryMaterial => SelectedLibraryMaterial != null;
    public bool CanDeleteMaterial => SelectedMaterial != null;
    public bool ShowMainContent => SelectedMaterial != null || IsCompareMode;
    public bool ShowEmptyState => !ShowMainContent;
    public bool IsElementSelected => SelectedElementInput != null;
    public bool HasValidDensity => IsCompareMode
            ? ComparedMaterials.Any(m => m.Density > 0)
            : SelectedMaterial?.Density > 0;

    public bool HasBuildupFactors => IsCompareMode
        ? ComparedMaterials.Any(m => BuildupCalc.GetGpParameters(m, 1.0, BuildupFactorType.Exposure) != null)
        : SelectedMaterial != null && BuildupCalc.GetGpParameters(SelectedMaterial, 1.0, BuildupFactorType.Exposure) != null;

    public IRelayCommand DeleteMaterialCommand => LibraryVM.DeleteMaterialCommand;
    public IRelayCommand SaveMaterialToLibraryCommand => LibraryVM.SaveMaterialToLibraryCommand;
    public IRelayCommand BeginRenameCustomMaterialCommand => LibraryVM.BeginRenameCustomMaterialCommand;
    public IRelayCommand CancelRenameCustomMaterialCommand => LibraryVM.CancelRenameCustomMaterialCommand;
    public IRelayCommand DeleteLibraryMaterialCommand => LibraryVM.DeleteLibraryMaterialCommand;
    public IRelayCommand SaveCustomMaterialNameCommand => LibraryVM.SaveCustomMaterialNameCommand;

    partial void OnSelectedInputTabIndexChanging(int value)
    {
        if (!IsLoadMode && SelectedInputTabIndex >= 0 && SelectedInputTabIndex < 3)
            _densityMemories[SelectedInputTabIndex] = MaterialDensityInput;
    }
    partial void OnSelectedInputTabIndexChanged(int value)
    {
        if (!IsLoadMode && value >= 0 && value < 3) MaterialDensityInput = _densityMemories[value];

        IsMixtureMode = (value == 2);

        OnPropertyChanged(nameof(IsCreateMode));
        UpdateMaterialName();
    }
    partial void OnSelectedLoadTabIndexChanged(int value)
    {
        ShowCustomMaterials = (value == 1);
    }

    partial void OnCompoundFormulaInputChanged(string value) => UpdateMaterialName();
    partial void OnMixtureInputChanged(string value) => UpdateMaterialName();
    partial void OnMaterialDensityInputChanged(string value) => UpdateMaterialName();
    partial void OnSelectedElementInputChanged(Element? value) { OnPropertyChanged(nameof(IsElementSelected)); UpdateMaterialName(); }
    partial void OnElementSearchTextChanged(string value) => ApplyElementFilter();

    partial void OnIsLoadModeChanging(bool value)
    {
        if (!IsLoadMode && value && SelectedInputTabIndex >= 0 && SelectedInputTabIndex < 3)
            _densityMemories[SelectedInputTabIndex] = MaterialDensityInput;
    }

    partial void OnIsLoadModeChanged(bool value)
    {
        if (value)
        {
            MaterialDensityInput = SelectedLibraryMaterial?.Density?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

            UpdatePreviewCompositionTable();
        }
        else if (SelectedInputTabIndex >= 0 && SelectedInputTabIndex < 3)
        {
            MaterialDensityInput = _densityMemories[SelectedInputTabIndex];
        }
        OnPropertyChanged(nameof(IsCreateMode));
        UpdateMaterialName();
    }
    #endregion

    #region Lists, Data Tables Forwarding & Initialization
    public ObservableCollection<Element> AvailableElements { get; } = new();
    public ObservableCollection<Element> FilteredElements { get; } = new();
    public ObservableCollection<SelectableItem> IsotopeOptions { get; } = new();
    public ObservableCollection<SelectableItem> SubshellOptions { get; } = new();
    public bool HasSubshells => SubshellOptions.Count > 0;

    public ObservableCollection<PreviewRow> PreviewCompositionTable { get; } = new();
    public ObservableCollection<CompositionRow> CompositionTable { get; } = new();

    public ObservableCollection<PhysicsTableRow>? CrossSectionData => TableVM.CrossSectionData;
    public ObservableCollection<ColumnHeader>? CrossSectionHeaders => TableVM.CrossSectionHeaders;

    public ObservableCollection<PhysicsTableRow>? MacData => TableVM.MacData;
    public ObservableCollection<ColumnHeader>? MacHeaders => TableVM.MacHeaders;

    public ObservableCollection<PhysicsTableRow>? LacData => TableVM.LacData;
    public ObservableCollection<ColumnHeader>? LacHeaders => TableVM.LacHeaders;

    public ObservableCollection<PhysicsTableRow>? DepthsData => TableVM.DepthsData;
    public ObservableCollection<ColumnHeader>? DepthsHeaders => TableVM.DepthsHeaders;

    public ObservableCollection<PhysicsTableRow>? ZeffData => TableVM.ZeffData;
    public ObservableCollection<ColumnHeader>? ZeffHeaders => TableVM.ZeffHeaders;

    public ObservableCollection<PhysicsTableRow>? BuildupData => TableVM.BuildupData;
    public ObservableCollection<ColumnHeader>? BuildupHeaders => TableVM.BuildupHeaders;

    public IRelayCommand CopyTableToClipboardCommand => TableVM.CopyCrossSectionTableCommand;
    public IRelayCommand CopyMacTableToClipboardCommand => TableVM.CopyMacTableCommand;
    public IRelayCommand CopyLacTableToClipboardCommand => TableVM.CopyLacTableCommand;
    public IRelayCommand CopyDepthsTableToClipboardCommand => TableVM.CopyDepthsTableCommand;
    public IRelayCommand CopyZeffTableToClipboardCommand => TableVM.CopyZeffTableCommand;
    public IRelayCommand CopyBuildupTableToClipboardCommand => TableVM.CopyBuildupTableCommand;

    public MainViewModel()
    {
        var services = App.Current.Services;
        _ptService = services.GetService<PeriodicTableService>()!;
        BuildupCalc = services.GetService<BuildupCalculator>()!;

        LibraryVM = services.GetService<MaterialLibraryViewModel>()!;
        ChartVM = services.GetService<ChartViewModel>()!;
        TableVM = services.GetService<DataTableViewModel>()!;

        ChartVM.RequestRepaint += (s, e) => RequestRepaint?.Invoke(this, EventArgs.Empty);
        LibraryVM.ShowAlert += (msg, title) => ShowAlert?.Invoke(msg, title);
        LibraryVM.RequestConfirmation += (title, msg, btnText) => RequestConfirmation?.Invoke(title, msg, btnText) ?? Task.FromResult(false);

        LibraryVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LibraryVM.SelectedLibraryMaterial))
            {
                OnPropertyChanged(nameof(SelectedLibraryMaterial));
                OnPropertyChanged(nameof(HasSelectedLibraryMaterial));

                if (IsLoadMode)
                {
                    MaterialDensityInput = LibraryVM.SelectedLibraryMaterial?.Density?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    OnPropertyChanged(nameof(IsCreateMode));
        UpdateMaterialName();
                    UpdatePreviewCompositionTable();
                }
            }
            else if (e.PropertyName == nameof(LibraryVM.ShowCustomMaterials))
            {
                OnPropertyChanged(nameof(ShowCustomMaterials));
                SelectedLoadTabIndex = LibraryVM.ShowCustomMaterials ? 1 : 0;
            }
        };

        Materials.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasMaterials));

            if (Materials.Count == 0 && IsCompareMode)
            {
                IsCompareMode = false;
            }
        };
    }

    public async Task InitializeDataAsync()
    {
        IsLoading = true;

        await Task.Run(() =>
        {
            string appBase = AppDomain.CurrentDomain.BaseDirectory;

            _ptService.LoadData(
                System.IO.Path.Combine(appBase, "Data", "Isotopes.csv"),
                System.IO.Path.Combine(appBase, "Data", "Elements.csv")
            );
        });

        foreach (var e in _ptService.GetAllElements())
        {
            AvailableElements.Add(e);
            FilteredElements.Add(e);
        }

        List<string> allLibraryFiles = new List<string>();

        string appBasePath = AppDomain.CurrentDomain.BaseDirectory;
        string defaultDataFolder = System.IO.Path.Combine(appBasePath, "Data");
        if (System.IO.Directory.Exists(defaultDataFolder))
        {
            allLibraryFiles.AddRange(System.IO.Directory.GetFiles(defaultDataFolder, "*.json"));
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userDataFolder = System.IO.Path.Combine(appData, "Panex", "Data");
        if (System.IO.Directory.Exists(userDataFolder))
        {
            allLibraryFiles.AddRange(System.IO.Directory.GetFiles(userDataFolder, "*.json"));
        }

        if (allLibraryFiles.Count > 0)
        {
            LibraryVM.LoadLibraries(allLibraryFiles);
        }
        SelectedElementInput = AvailableElements.FirstOrDefault(e => e.Symbol == "H");
        CurrentPlotSettings = CrossSectionSettings;
        OnPropertyChanged(nameof(IsCreateMode));
        UpdateMaterialName();

        IsLoading = false;
    }

    private void ApplyElementFilter()
    {
        var previous = SelectedElementInput;
        string query = ElementSearchText?.Trim() ?? string.Empty;

        List<Element> matches;
        if (string.IsNullOrWhiteSpace(query))
        {
            matches = AvailableElements.ToList();
        }
        else
        {
            matches = AvailableElements
                .Where(e =>
                    e.Z.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Z)
                .ToList();
        }

        FilteredElements.Clear();
        foreach (var el in matches)
        {
            FilteredElements.Add(el);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var bestMatch = matches
                .OrderBy(e =>
                {
                    if (e.Z.ToString().Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
                    if (e.Symbol.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1;
                    if (e.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 2;
                    if (e.Symbol.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
                    if (e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 4;
                    if (e.Z.ToString().StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 5;
                    return 6;
                })
                .ThenBy(e => e.Z)
                .FirstOrDefault();

            SelectedElementInput = bestMatch;
        }
        else
        {
            SelectedElementInput = (previous != null && FilteredElements.Contains(previous))
                ? previous
                : (FilteredElements.FirstOrDefault(e => e.Symbol == "H") ?? FilteredElements.FirstOrDefault());
        }
    }

    private void UpdatePreviewCompositionTable()
    {
        PreviewCompositionTable.Clear();
        if (SelectedLibraryMaterial == null) return;

        foreach (var kvp in SelectedLibraryMaterial.Composition)
        {
            PreviewCompositionTable.Add(new PreviewRow
            {
                Symbol = kvp.Key.Symbol,
                FractionText = kvp.Value.ToString("P4")
            });
        }
    }
    #endregion

    #region UI & Interaction Logic
    [RelayCommand]
    private async Task CreateMaterial()
    {
        await Task.Delay(200); // Yield briefly so the drawer close animation runs smoothly

        double? density = null;
        if (!string.IsNullOrWhiteSpace(MaterialDensityInput))
        {
            if (double.TryParse(MaterialDensityInput, out double d)) density = d;
            else { ShowAlert?.Invoke("Density must be a valid number.", "Error"); return; }
        }

        try
        {
            Material? newMat = null;
            if (IsLoadMode && SelectedLibraryMaterial != null)
            {
                newMat = new Material
                {
                    Name = SelectedLibraryMaterial.Name,
                    Density = density ?? SelectedLibraryMaterial.Density,
                    Composition = SelectedLibraryMaterial.Composition.ToDictionary(k => k.Key, v => v.Value)
                };
            }
            else
            {
                if (SelectedInputTabIndex == 0 && SelectedElementInput != null) newMat = _ptService.CreateFromElement(MaterialNameInput, SelectedElementInput, density);
                else if (SelectedInputTabIndex == 1) newMat = _ptService.CreateFromCompound(MaterialNameInput, CompoundFormulaInput, density);
                else if (SelectedInputTabIndex == 2) newMat = _ptService.CreateFromMixture(MaterialNameInput, MixtureInput, density, IsMixtureByWeight);
            }

            if (newMat != null)
            {
                Materials.Add(newMat);
                SelectedMaterial = newMat;
                MaterialCreated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) { ShowAlert?.Invoke($"Error:\n{ex.Message}", "Error"); }
    }

    public void SortLibrary(string columnName, bool ascending) => LibraryVM.SortLibrary(columnName, ascending);

    public bool IsAllIsotopesSelected
    {
        get => IsotopeOptions.Count > 0 && IsotopeOptions.All(i => i.IsSelected);
        set { if (IsAllIsotopesSelected == value) return; _suppressRefresh = true; foreach (var item in IsotopeOptions) item.IsSelected = value; _suppressRefresh = false; OnPropertyChanged(); TableVM.ClearCaches(); RefreshUIOnly(); }
    }

    public bool IsAllSubshellsSelected
    {
        get => SubshellOptions.Count > 0 && SubshellOptions.All(s => s.IsSelected);
        set { if (IsAllSubshellsSelected == value) return; _suppressRefresh = true; foreach (var item in SubshellOptions) item.IsSelected = value; _suppressRefresh = false; OnPropertyChanged(); TableVM.ClearCaches(); RefreshUIOnly(); }
    }

    private void OnItemChanged()
    {
        if (_suppressRefresh) return;
        OnPropertyChanged(nameof(IsAllIsotopesSelected));
        OnPropertyChanged(nameof(IsAllSubshellsSelected));
        TableVM.ClearCaches();
        RefreshUIOnly();
    }

    [RelayCommand]
    private void ToggleMaterialCompare()
    {
        if (IsCompareMode)
        {
            OnPropertyChanged(nameof(HasValidDensity));
            OnPropertyChanged(nameof(HasBuildupFactors));

            RefreshUIOnly();
        }
    }

    private bool _suppressRefresh = false;
    private System.Threading.CancellationTokenSource? _refreshCts;

    private void OnSelectedMaterialChanged(Material? value)
    {
        if (value == null)
        {
            IsotopeOptions.Clear();
            SubshellOptions.Clear();
            TableVM.ClearCaches();

            OnPropertyChanged(nameof(CanDeleteMaterial));
            OnPropertyChanged(nameof(HasValidDensity));
            OnPropertyChanged(nameof(HasBuildupFactors));
            DeleteMaterialCommand.NotifyCanExecuteChanged();

            RequestRepaint?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsLoading = true;
        _ = ProcessNewMaterialUIAsync();
    }

    private void ValidateCurrentTab()
    {
        if ((SelectedMainTabIndex == 2 || SelectedMainTabIndex == 3) && !HasValidDensity)
        {
            SelectedMainTabIndex = 0;
        }
        else if (SelectedMainTabIndex == 5 && !HasBuildupFactors)
        {
            SelectedMainTabIndex = 0;
        }
    }

    private async Task ProcessNewMaterialUIAsync()
    {
        OnPropertyChanged(nameof(CanDeleteMaterial));
        OnPropertyChanged(nameof(HasValidDensity));
        OnPropertyChanged(nameof(HasBuildupFactors));
        DeleteMaterialCommand.NotifyCanExecuteChanged();

        ValidateCurrentTab();

        await Task.Delay(50);
        PopulateDetailedOptions();
        UpdateCompositionTable();

        await RefreshAllAsync();
    }

    private void RefreshAll() => _ = RefreshAllAsync();

    private async Task RefreshAllAsync()
    {
        if (_suppressRefresh) return;
        _refreshCts?.Cancel();
        _refreshCts = new System.Threading.CancellationTokenSource();
        var token = _refreshCts.Token;

        var currentMaterial = SelectedMaterial;

        if (currentMaterial == null && !IsCompareMode) { IsLoading = false; RequestRepaint?.Invoke(this, EventArgs.Empty); return; }

        IsLoading = true;
        try
        {
            await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return;

                if (currentMaterial != null)
                {
                    currentMaterial.GetTotalCrossSectionCurve();
                    if (token.IsCancellationRequested) return;
                    currentMaterial.GetEffectiveAtomicNumberCurve();
                    if (token.IsCancellationRequested) return;
                    currentMaterial.GetPhotonuclearCrossSectionCurve();
                    if (token.IsCancellationRequested) return;
                    if (currentMaterial.Density > 0) currentMaterial.GetTotalMassAttenuationCurve();
                }
            }, token);

            if (token.IsCancellationRequested) return;

            // Always update the Absorption Edges flyout regardless of the active tab
            if (IsCompareMode)
            {
                TableVM.AbsorptionEdgesData = null;
            }
            else if (currentMaterial != null)
            {
                TableVM.UpdateAbsorptionEdgesTable(currentMaterial);
            }
            OnPropertyChanged(nameof(AbsorptionEdgesData));

            if (ShowGrid) UpdateTables();

            RequestRepaint?.Invoke(this, EventArgs.Empty);
            await Task.Delay(50, token);
        }
        catch (TaskCanceledException) { }
        catch (Exception ex) { ShowAlert?.Invoke($"Error calculating material data:\n{ex.Message}", "Calculation Error"); }
        finally { if (!token.IsCancellationRequested) IsLoading = false; }
    }

    private void RefreshUIOnly()
    {
        if (_suppressRefresh) return;
        if (ShowGrid) UpdateTables();
        RequestRepaint?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeDatabaseLibrary(string libraryType)
    {
        IsLoading = true;

        _ptService.SwitchPhotoatomicLibrary(libraryType);

        foreach (var mat in Materials) mat.ClearCaches();
        foreach (var mat in MaterialLibrary) mat.ClearCaches();

        TableVM.ClearCaches();

        // Refresh subshell and isotope options for the newly loaded library
        PopulateDetailedOptions();

        RefreshAll();
    }

    private void PopulateDetailedOptions()
    {
        IsotopeOptions.Clear();
        SubshellOptions.Clear();
        if (SelectedMaterial == null) return;

        foreach (var kvp in SelectedMaterial.Composition)
        {
            foreach (var iso in kvp.Key.Isotopes.Where(i => i.PhotonuclearData != null))
                IsotopeOptions.Add(new SelectableItem($"{kvp.Key.Symbol}-{iso.A} (PN)", OnItemChanged) { Z = iso.Z, A = iso.A, IsIsotope = true });

            foreach (var p in kvp.Key.PhotoatomicData.Where(x => x.C == 73 && x.S == 91))
            {
                double edgeKeV = p.Energies.FirstOrDefault() * 1000.0;
                string displayName = $"{kvp.Key.Symbol} {p.SubshellName} ({edgeKeV:F2} keV)";
                SubshellOptions.Add(new SelectableItem(displayName, OnItemChanged) { Z = kvp.Key.Z, SubshellIndex = p.X, IsSubshell = true });
            }
        }

        OnPropertyChanged(nameof(IsAllIsotopesSelected));
        OnPropertyChanged(nameof(IsAllSubshellsSelected));

        OnPropertyChanged(nameof(HasSubshells));
    }

    [RelayCommand]
    private void CopyIsotopes()
    {
        if (SelectedElementInput == null || SelectedElementInput.Isotopes.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("A\tMass (u)\tAbundance");

        foreach (var iso in SelectedElementInput.Isotopes)
        {
            sb.AppendLine($"{iso.A}\t{iso.Mass:F5}\t{iso.Abundance:P3}");
        }

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    [RelayCommand]
    private void CopyPreviewComposition()
    {
        if (PreviewCompositionTable == null || PreviewCompositionTable.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Symbol\tFraction");

        foreach (var row in PreviewCompositionTable)
        {
            sb.AppendLine($"{row.Symbol}\t{row.FractionText}");
        }

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    #endregion

    #region Plotting & Table Routing Logic
    public void PlotTo(ScottPlot.Plot plot)
    {
        if (IsCompareMode) ChartVM.PlotCrossSectionComparisonTo(plot, ComparedMaterials, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
        else ChartVM.PlotCrossSectionTo(plot, SelectedMaterial, ShowTotal, ShowCoherent, ShowIncoherent, ShowPhotoelectric, ShowPair, ShowTriplet, ShowPhotonuclear, IsotopeOptions, SubshellOptions);
    }

    public void PlotMacTo(ScottPlot.Plot plot)
    {
        if (IsCompareMode) ChartVM.PlotMacComparisonTo(plot, ComparedMaterials, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
        else ChartVM.PlotMacTo(plot, SelectedMaterial, ShowTotal, ShowCoherent, ShowIncoherent, ShowPhotoelectric, ShowPair, ShowTriplet, ShowPhotonuclear, IsotopeOptions, SubshellOptions);
    }

    public void PlotLacTo(ScottPlot.Plot plot)
    {
        if (IsCompareMode) ChartVM.PlotLacComparisonTo(plot, ComparedMaterials, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
        else ChartVM.PlotLacTo(plot, SelectedMaterial, ShowTotal, ShowCoherent, ShowIncoherent, ShowPhotoelectric, ShowPair, ShowTriplet, ShowPhotonuclear, IsotopeOptions, SubshellOptions);
    }

    public void PlotDepthTo(ScottPlot.Plot plot)
    {
        if (IsCompareMode) ChartVM.PlotDepthComparisonTo(plot, ComparedMaterials, NValue, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
        else ChartVM.PlotDepthTo(plot, SelectedMaterial, NValue, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
    }

    public void PlotZeffTo(ScottPlot.Plot plot)
    {
        if (IsCompareMode) ChartVM.PlotZeffComparisonTo(plot, ComparedMaterials, CompareUseInterpolatedZeff, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
        else ChartVM.PlotZeffTo(plot, SelectedMaterial);
    }

    public void PlotZeqTo(ScottPlot.Plot plot)
    {
        if (IsCompareMode) ChartVM.PlotZeqComparisonTo(plot, ComparedMaterials);
        else ChartVM.PlotZeqTo(plot, SelectedMaterial);
    }

    public void PlotBuildupTo(ScottPlot.Plot plot)
    {
        if (IsCompareMode) ChartVM.PlotBuildupComparisonTo(plot, ComparedMaterials, TargetMfps.FirstOrDefault(), SelectedBuildupType);
        else ChartVM.PlotBuildupTo(plot, SelectedMaterial, TargetMfps, SelectedBuildupType);
    }

    private List<double> GetEnergyGrid()
    {
        List<double> energies = new();

        if (IsUserGrid)
        {
            foreach (var p in UserGridInput.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double e)) energies.Add(e);
        }
        else if (SelectedMaterial != null) energies.AddRange(SelectedMaterial.GetMasterEnergyGrid());

        return energies.Where(e => e >= 0.001).Distinct().OrderBy(e => e).ToList();
    }

    [RelayCommand]
    private void UpdateTables()
    {
        var energies = GetEnergyGrid();

        if (IsCompareMode)
        {
            TableVM.UpdateCrossSectionComparisonTable(ComparedMaterials, energies, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
            TableVM.UpdateMacComparisonTable(ComparedMaterials, energies, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
            TableVM.UpdateLacComparisonTable(ComparedMaterials, energies, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
            TableVM.UpdateDepthsComparisonTable(ComparedMaterials, energies, NValue, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
            TableVM.UpdateZeffComparisonTable(ComparedMaterials, energies, CompareUseInterpolatedZeff, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
            TableVM.UpdateBuildupComparisonTable(ComparedMaterials, TargetMfps.FirstOrDefault(), SelectedBuildupType);
        }
        else
        {
            if (SelectedMaterial == null) return;

            TableVM.UpdateCrossSectionDataTable(SelectedMaterial, energies, ShowTotal, ShowCoherent, ShowIncoherent, ShowPhotoelectric, ShowPair, ShowTriplet, ShowPhotonuclear, IsotopeOptions, SubshellOptions);
            TableVM.UpdateMacDataTable(SelectedMaterial, energies, ShowTotal, ShowCoherent, ShowIncoherent, ShowPhotoelectric, ShowPair, ShowTriplet, ShowPhotonuclear, IsotopeOptions, SubshellOptions);
            TableVM.UpdateLacDataTable(SelectedMaterial, energies, ShowTotal, ShowCoherent, ShowIncoherent, ShowPhotoelectric, ShowPair, ShowTriplet, ShowPhotonuclear, IsotopeOptions, SubshellOptions);
            TableVM.UpdateDepthsDataTable(SelectedMaterial, energies, NValue, DepthIncludePhotoelectric, DepthIncludeCoherent, DepthIncludeIncoherent, DepthIncludePair, DepthIncludeTriplet, DepthIncludePhotonuclear);
            TableVM.UpdateZeffDataTable(SelectedMaterial, energies);
            TableVM.UpdateBuildupDataTable(SelectedMaterial, TargetMfps, SelectedBuildupType);
        }

        OnPropertyChanged(nameof(CrossSectionData));
        OnPropertyChanged(nameof(MacData));
        OnPropertyChanged(nameof(LacData));
        OnPropertyChanged(nameof(DepthsData));
        OnPropertyChanged(nameof(ZeffData));
        OnPropertyChanged(nameof(BuildupData));

        OnPropertyChanged(nameof(CrossSectionHeaders));
        OnPropertyChanged(nameof(MacHeaders));
        OnPropertyChanged(nameof(LacHeaders));
        OnPropertyChanged(nameof(DepthsHeaders));
        OnPropertyChanged(nameof(ZeffHeaders));
        OnPropertyChanged(nameof(BuildupHeaders));
    }

    private void UpdateMaterialName()
    {
        if (IsLoadMode) { MaterialNameInput = SelectedLibraryMaterial?.Name ?? "Select a Material"; return; }

        string baseName = "New Material";

        if (SelectedInputTabIndex == 0) baseName = SelectedElementInput != null ? $"{SelectedElementInput.Name} ({SelectedElementInput.Symbol})" : "Select an Element";
        else if (SelectedInputTabIndex == 1) baseName = string.IsNullOrWhiteSpace(CompoundFormulaInput) ? "New Compound" : CompoundFormulaInput;
        else if (SelectedInputTabIndex == 2)
        {
            int count = MixtureInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            baseName = count > 0 ? $"Mixture ({count} components)" : "New Mixture";
        }

        MaterialNameInput = baseName;
    }

    private void UpdateCompositionTable()
    {
        CompositionTable.Clear();
        if (SelectedMaterial == null) return;

        double totalMass = SelectedMaterial.Composition.Sum(x => x.Key.AtomicMass * x.Value);

        foreach (var kvp in SelectedMaterial.Composition)
        {
            CompositionTable.Add(new CompositionRow
            {
                Z = kvp.Key.Z,
                Symbol = kvp.Key.Symbol,
                Name = kvp.Key.Name,
                AtomicMass = kvp.Key.AtomicMass,
                AtomFraction = kvp.Value,
                MassFraction = (kvp.Key.AtomicMass * kvp.Value) / totalMass
            });
        }
    }

    [RelayCommand]
    private void CopyComposition()
    {
        if (CompositionTable == null || CompositionTable.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Z\tSymbol\tElement\tAtomic Mass (u)\tAtom %\tMass %");

        foreach (var row in CompositionTable) sb.AppendLine($"{row.Z}\t{row.Symbol}\t{row.Name}\t{row.AtomicMassString}\t{row.AtomFractionString}\t{row.MassFractionString}");

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
    #endregion
}