namespace Panex.Models;

public class Isotope
{
    public int Z { get; set; }
    public int A { get; set; }
    public double Abundance { get; set; }
    public double Mass { get; set; }

    // Photonuclear cross section (JENDL-5)
    public CrossSectionCurve? PhotonuclearData { get; set; }

    public double GetPhotonuclearCrossSection(double energyMeV)
    {
        return PhotonuclearData?.Interpolate(energyMeV) ?? 0.0;
    }
}