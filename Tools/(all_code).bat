@echo off
setlocal EnableDelayedExpansion

set "OUT=all_code.txt"
> "%OUT%" echo.
set "MISSING="

for %%F in (
    "..\Panex\Controls\InteractionFiltersControl.xaml"
    "..\Panex\Controls\InteractionFiltersControl.xaml.cs"
    "..\Panex\Converters\BoolToVisibilityConverter.cs"
    "..\Panex\Converters\BuildupFactorTypeToBoolConverter.cs"
    "..\Panex\Converters\InverseBooleanConverter.cs"
    "..\Panex\Converters\StringFormatConverter.cs"
    "..\Panex\Models\BuildupFactorModels.cs"
    "..\Panex\Models\CrossSectionCurve.cs"
    "..\Panex\Models\Element.cs"
    "..\Panex\Models\Isotope.cs"
    "..\Panex\Models\Material.cs"
    "..\Panex\Models\NaturalCubicSpline.cs"
    "..\Panex\Models\PhysicsConstants.cs"
    "..\Panex\Services\AttenuationCalculator.cs"
    "..\Panex\Services\BuildupCalculator.cs"
    "..\Panex\Services\BuildupDataService.cs"
    "..\Panex\Services\IPhotoatomicDataService.cs"
    "..\Panex\Services\PeriodicTableService.cs"
    "..\Panex\Services\PhotoatomicDataService.cs"
    "..\Panex\Services\PhotonuclearDataService.cs"
    "..\Panex\Services\XcomDataService.cs"
    "..\Panex\ViewModels\ChartViewModel.cs"
    "..\Panex\ViewModels\CompositionRow.cs"
    "..\Panex\ViewModels\CurveStyle.cs"
    "..\Panex\ViewModels\DataTableViewModel.cs"
    "..\Panex\ViewModels\MainViewModel.cs"
    "..\Panex\ViewModels\MaterialLibraryViewModel.cs"
    "..\Panex\ViewModels\PlotSettings.cs"
    "..\Panex\ViewModels\SelectableItem.cs"
    "..\Panex\ViewModels\UIModels.cs"
    "..\Panex\app.manifest"
    "..\Panex\App.xaml"
    "..\Panex\App.xaml.cs"
    "..\Panex\MainWindow.xaml"
    "..\Panex\MainWindow.xaml.cs"
    "..\Panex\Package.appxmanifest"
    "..\Panex\Panex.csproj"
    "..\Panex\Panex.csproj.user"
    "..\Panex.slnx"
) do (
    if exist "%%~F" (
        echo ===== %%~F =====>> "%OUT%"
        type "%%~F" >> "%OUT%"
        echo. >> "%OUT%"
    ) else (
        echo File not found: %%~F
        set "MISSING=!MISSING!%%~F;"
    )
)

echo.
if defined MISSING (
    echo ===== Missing Files Summary =====
    for %%M in (!MISSING!) do echo %%M
) else (
    echo All files were found.
)

echo.
echo Done. Output: %OUT%
pause
endlocal