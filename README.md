# Panex — Photon Attenuation & Cross-Section Studio

Panex is a native Windows desktop application for calculating, visualizing, and comparing photon interaction cross sections, attenuation coefficients, shielding depths, effective atomic numbers (*Z*<sub>eff</sub>), and gamma-ray buildup factors across elements, chemical compounds, custom mixtures, and standard reference materials.

Built with C# (.NET 8) and WinUI 3, Panex brings together **LLNL EPDL (EPICS2025)** and **NIST XCOM** photoatomic cross-section models alongside **JENDL-5** photonuclear data and **ANSI/ANS-6.4.3** buildup factor parameters in a single interactive workspace.

<!-- Optional: Add application screenshots before or after publishing the release
## Screenshots

![Panex Main Workspace](docs/screenshot.png)
-->

---

## Key Features

- **Dual Photoatomic Cross-Section Libraries (Switchable Live)**
  - **LLNL EPDL (EPICS2025)** (`Z = 1–100`, up to `100 GeV`), including individual atomic photoelectric subshell curves and absorption edges.
  - **NIST XCOM** (`Z = 1–100`, `1 keV – 100 GeV`), implementing XCOM log-log cubic spline interpolation, threshold-reduced variables for pair/triplet production, and piecewise hybrid photoelectric interpolation.
- **Photonuclear & Isotopic Contributions**
  - Incorporates **JENDL-5** photonuclear total photoabsorption cross sections (`MT=3` / `MT=5`) across 286 nuclides (`C=0, S=0, X=0`), with per-isotope filtering in plots and data tables.
- **Material Builder & Reference Libraries**
  - Create materials from single **Elements** (`100` elements, `Z = 1–100`), **Compounds** (chemical formula parser, e.g., `H2O`, `NaCl`), or **Mixtures** (atom fraction or weight/mass fraction).
  - Load pre-defined materials from **NIST SRD-124 ESTAR** (`117` materials) and **PNNL-15870 Rev. 2** (`411` materials), or manage persistent custom materials under `%LOCALAPPDATA%\Panex\Data\Material_User.json`.
- **Six Synchronized Physics Tabs (Interactive Charts + Data Tables)**
  1. **Cross Section (`barns/atom`)** — Total, Photoelectric Absorption (with EPDL subshell filter), Coherent (Rayleigh), Incoherent (Compton), Pair (Nuclear), Pair (Electron / Triplet), and Photonuclear (with isotope filter).
  2. **Mass Attenuation (`cm²/g`)** — Mass attenuation coefficient (`μ/ρ`) across the same component and filter controls.
  3. **Linear Attenuation (`cm⁻¹`)** — Linear attenuation coefficient (`μ`) for materials with positive density (`ρ > 0`).
  4. **Depths (`cm`)** — Mean Free Path (`MFP`), Half-Value Layer (`HVL`), Tenth-Value Layer (`TVL`), and arbitrary *n*-th value layer with configurable interaction components.
  5. **Effective Z (*Z*<sub>eff</sub>)** — Energy-dependent effective atomic number, supporting **Direct** and **Interpolated** methods in Comparison Mode.
  6. **Buildup Factors** — ANSI/ANS-6.4.3 Geometric Progression (G-P) **Exposure Buildup Factor (EBF)**, **Energy Absorption Buildup Factor (EABF)**, and **Equivalent Atomic Number (*Z*<sub>eq</sub>)** at user-defined MFP depths.
- **Multi-Material Comparison Mode** — Overlay and tabulate multiple checked materials side by side across all six physics tabs.
- **Diagnostics & Data Export**
  - Material composition inspector (Z, symbol, atomic mass, atom %, mass %) and photoelectric X-ray absorption edge table (`keV` / `MeV`).
  - Powered by **ScottPlot 5** (`ScottPlot.WinUI`): log/linear axis toggles, hover crosshair & energy readout tooltip, custom axis bounds, per-curve styling (color, pattern, width), Light/Dark theme persistence, **SVG/PNG** plot export, and tab-delimited clipboard copy using either the collated natural physics grid or custom energy lists (`MeV`).

---

## System Requirements

### Running the Release Installer (`Setup.exe`)
- **OS:** Windows 10 version 1809 (`10.0.17763.0`) or later / Windows 11 (`x64`)
- **Runtime:** None required — `.NET 8` and `Windows App SDK 1.8` are bundled self-contained in the release installer (`win-x64`).

### Building from Source
- **SDK:** `.NET 8.0 SDK` (`net8.0-windows10.0.19041.0`)
- **Platform Tooling:** Windows 10/11 SDK (`10.0.26100`) and Visual Studio 2022 with Desktop C# / WinUI 3 (Windows App SDK) tooling
- **Optional (Installer Packaging):** [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`ISCC.exe`)

---

## Installation

### Option 1 — Windows Installer (`Setup.exe`)
1. Download `Panex-v<version>-win-x64-Setup.exe` from the [GitHub Releases](https://github.com/hilafrederick/Panex/releases) page.
2. Run the installer, review and accept the Apache License 2.0 terms, and follow the setup wizard.
3. Launch **Panex** from the Start Menu or Desktop shortcut (`LICENSE` and `NOTICE` are installed in the application folder; custom materials and user preferences are stored per user in `%LOCALAPPDATA%\Panex\`).

### Option 2 — Build from Source
```powershell
git clone https://github.com/hilafrederick/Panex.git
cd Panex

# Restore and build (Release, x64)
dotnet build Panex/Panex.csproj -c Release -p:Platform=x64

# Publish a self-contained x64 build
dotnet publish Panex/Panex.csproj -c Release -p:PublishProfile=win-x64 -p:WindowsAppSDKSelfContained=true
```
*(Note: Running `build\build-release.bat` automates self-contained publishing, satellite language trimming, and installer packaging into `dist/` using `build\InnoSetup.iss`.)*

---

## Scientific Data Sources & Attribution

Bundled nuclear, atomic, and material datasets live under [`Panex/Data/`](Panex/Data/) and are documented in [`NOTICE`](NOTICE):

| Dataset / File | Description | Source & Reference |
| :--- | :--- | :--- |
| `PA_EPDL_2025/` | Photoatomic cross sections & subshell absorption edges (`Z = 1–100`) | LLNL Evaluated Photon Data Library (EPDL / EPICS2025) |
| `PA_NIST_XCOM/` | Photon cross sections, thresholds & hybrid grids (`Z = 1–100`) | NIST XCOM v3.1 (M. J. Berger & J. H. Hubbell, NBSIR-87) |
| `PN_JENDL5/` | Photonuclear total photoabsorption cross sections (`pn_jendl5_xs.csv`; `286` nuclides, `MT=3` / `MT=5`) | JENDL-5 Photonuclear Data File, JAEA Nuclear Data Center |
| `ANSI_EBF.csv`, `ANSI_EABF.csv` | G-P buildup factor parameters (`23` elements, `0.015–15.0 MeV`) | ANSI/ANS-6.4.3 Standard |
| `Material_NIST-ESTAR.json` | Reference elements & compounds (`117` entries) | [NIST SRD-124](https://dx.doi.org/10.18434/T4NC7P) |
| `Material_PNNL-15870-Rev2.json` | Compendium material compositions (`411` entries) | Detwiler et al., [PNNL-15870 Rev. 2](https://doi.org/10.2172/1782721) (Apr. 2021) |
| `Elements.csv`, `Isotopes.csv` | Atomic weights, natural abundances & isotopic masses | Standard elemental & isotopic reference tables |

---

## License

- **Panex Application Source Code:** Licensed under the **Apache License, Version 2.0** — see [`LICENSE`](LICENSE).
- **Datasets & Third-Party Libraries:** Bundled reference datasets (`Panex/Data/`) and NuGet dependencies (`CommunityToolkit.Mvvm`, `CommunityToolkit.WinUI`, `Microsoft.Extensions.DependencyInjection`, `ScottPlot.WinUI`, `SkiaSharp`, `HarfBuzzSharp`, `WinUIEx`, `Windows App SDK`) remain governed by their respective original terms, licenses, and citations — see [`NOTICE`](NOTICE).

---

## Contributing & Citing

Contributions, bug reports, and pull requests are welcome via [GitHub Issues](https://github.com/hilafrederick/Panex/issues) and Pull Requests. Contributions submitted to this repository are governed by Section 5 of the Apache License 2.0.

If you use Panex in published research or engineering reports, please cite **Panex** alongside the underlying nuclear/atomic data libraries used in your calculation (LLNL EPDL / EPICS2025, NIST XCOM, JENDL-5, ANSI/ANS-6.4.3, PNNL-15870 Rev. 2, and/or NIST SRD-124).
