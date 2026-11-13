using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Panex.Models;
using Panex.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Panex.ViewModels;

public partial class MaterialLibraryViewModel : ObservableObject
{
    private readonly PeriodicTableService _ptService;

    public event Action<string, string>? ShowAlert;
    public event Func<string, string, string, Task<bool>>? RequestConfirmation;

    [ObservableProperty] private Material? selectedMaterial;
    [ObservableProperty] private Material? selectedLibraryMaterial;
    [ObservableProperty] private string librarySearchText = "";
    [ObservableProperty] private bool showCustomMaterials = false;
    [ObservableProperty] private bool isRenamingCustomMaterial = false;

    public ObservableCollection<Material> Materials { get; } = new();
    public ObservableCollection<Material> MaterialLibrary { get; } = new();
    [ObservableProperty] private ObservableCollection<Material> filteredLibrary = new();

    private string _currentSortColumn = "Name";
    private bool _isSortAscending = true;
    private string _originalCustomMaterialName = "";

    public bool IsNotRenamingCustomMaterial => !IsRenamingCustomMaterial;
    public bool HasSelectedLibraryMaterial => SelectedLibraryMaterial != null;
    public bool CanDeleteMaterial => SelectedMaterial != null;

    public MaterialLibraryViewModel(PeriodicTableService ptService)
    {
        _ptService = ptService;
    }

    partial void OnLibrarySearchTextChanged(string value) => ApplyLibraryFilter();
    partial void OnShowCustomMaterialsChanged(bool value) => ApplyLibraryFilter();
    partial void OnIsRenamingCustomMaterialChanged(bool value) => OnPropertyChanged(nameof(IsNotRenamingCustomMaterial));

    partial void OnSelectedLibraryMaterialChanged(Material? value)
    {
        IsRenamingCustomMaterial = false;
        OnPropertyChanged(nameof(HasSelectedLibraryMaterial));
    }

    public void LoadLibraries(IEnumerable<string> jsonFilePaths)
    {
        foreach (var file in jsonFilePaths)
        {
            foreach (var mat in _ptService.LoadMaterialsFromJson(file))
            {
                MaterialLibrary.Add(mat);
            }
        }
        ApplyLibraryFilter();
    }

    public void SortLibrary(string columnName, bool ascending)
    {
        _currentSortColumn = columnName;
        _isSortAscending = ascending;
        ApplyLibraryFilter();
    }

    private void ApplyLibraryFilter()
    {
        var tempList = MaterialLibrary.Where(m =>
            (m.Category == "My Custom Materials" == ShowCustomMaterials) &&
            (string.IsNullOrWhiteSpace(LibrarySearchText) || m.Name.Contains(LibrarySearchText, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        IEnumerable<Material> sortedList = _currentSortColumn switch
        {
            "Category" => _isSortAscending ? tempList.OrderBy(m => m.Category).ThenBy(m => m.Name) : tempList.OrderByDescending(m => m.Category).ThenBy(m => m.Name),
            "Density" => _isSortAscending ? tempList.OrderBy(m => m.Density ?? 0) : tempList.OrderByDescending(m => m.Density ?? 0),
            "Reference" => _isSortAscending ? tempList.OrderBy(m => m.Reference).ThenBy(m => m.Name) : tempList.OrderByDescending(m => m.Reference).ThenBy(m => m.Name),
            _ => _isSortAscending ? tempList.OrderBy(m => m.Name) : tempList.OrderByDescending(m => m.Name),
        };

        FilteredLibrary.Clear();
        foreach (var m in sortedList) FilteredLibrary.Add(m);
        if (SelectedLibraryMaterial == null || !FilteredLibrary.Contains(SelectedLibraryMaterial))
            SelectedLibraryMaterial = FilteredLibrary.FirstOrDefault();
    }

    [RelayCommand]
    private void DeleteMaterial(Material? materialToDelete)
    {
        var target = materialToDelete ?? SelectedMaterial;
        if (target != null)
        {
            Materials.Remove(target);
            if (SelectedMaterial == target) SelectedMaterial = null;
        }
    }

    [RelayCommand]
    private async Task SaveMaterialToLibrary(Material? materialToSave)
    {
        var target = materialToSave ?? SelectedMaterial;
        if (target == null) return;

        try
        {
            target.Category = "My Custom Materials";
            target.Reference = "Material_User";

            var existing = MaterialLibrary.FirstOrDefault(m => m.Name == target.Name && m.Category == "My Custom Materials");
            if (existing != null)
            {
                if (RequestConfirmation != null)
                {
                    bool confirm = await RequestConfirmation.Invoke("Overwrite Material", $"A custom material named '{target.Name}' already exists. Do you want to overwrite it?", "Overwrite");
                    if (!confirm) return;
                }

                MaterialLibrary.Remove(existing);
            }

            MaterialLibrary.Add(target);
            ForceSaveCustomLibrary();
            LibrarySearchText = "";
            ShowCustomMaterials = true;
            ApplyLibraryFilter();

            ShowAlert?.Invoke($"'{target.Name}' saved to your custom library!", "Success");
        }
        catch (Exception ex)
        {
            ShowAlert?.Invoke($"Could not save material:\n{ex.Message}", "Error");
        }
    }
    public void ForceSaveCustomLibrary()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userFilePath = System.IO.Path.Combine(appData, "Panex", "Data", "Material_User.json");

        string? directory = System.IO.Path.GetDirectoryName(userFilePath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        var customMaterials = MaterialLibrary.Where(m => m.Category == "My Custom Materials");
        _ptService.SaveMaterialsToJson(customMaterials, userFilePath);
        ApplyLibraryFilter();
    }

    [RelayCommand]
    private void BeginRenameCustomMaterial()
    {
        if (SelectedLibraryMaterial?.Category == "My Custom Materials")
        {
            _originalCustomMaterialName = SelectedLibraryMaterial.Name;
            IsRenamingCustomMaterial = true;
        }
    }

    [RelayCommand]
    private void CancelRenameCustomMaterial()
    {
        if (SelectedLibraryMaterial != null) SelectedLibraryMaterial.Name = _originalCustomMaterialName;
        IsRenamingCustomMaterial = false;
    }

    [RelayCommand]
    private async Task DeleteLibraryMaterial(Material mat)
    {
        if (mat == null || mat.Category != "My Custom Materials") return;
        if (RequestConfirmation != null)
        {
            bool confirm = await RequestConfirmation.Invoke("Delete Custom Material", $"Are you sure you want to permanently delete '{mat.Name}'? This action cannot be undone.", "Delete");
            if (!confirm) return;
        }

        MaterialLibrary.Remove(mat);
        ForceSaveCustomLibrary();
    }

    [RelayCommand]
    private void SaveCustomMaterialName()
    {
        IsRenamingCustomMaterial = false;
        ForceSaveCustomLibrary();
    }
}