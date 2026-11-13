// File: \Models\Isotope.cs
namespace Panex.Models;

public class Isotope
{
    public int Z { get; set; }
    public int A { get; set; }
    public double Abundance { get; set; }
    public double Mass { get; set; }

    // NEW: Photonuclear Cross Section (JENDL-5)
    public CrossSectionCurve? PhotonuclearData { get; set; }

    public double GetPhotonuclearCrossSection(double energyMeV)
    {
        return PhotonuclearData?.Interpolate(energyMeV) ?? 0.0;
    }
}