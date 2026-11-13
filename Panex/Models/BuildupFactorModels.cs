namespace Panex.Models;

/// <summary>
/// Defines the type of buildup factor to calculate.
/// </summary>
public enum BuildupFactorType
{
    Exposure,          // EBF
    EnergyAbsorption   // EABF
}

/// <summary>
/// Holds the ANSI/ANS-6.4.3 Geometric Progression (G-P) fitting parameters for a specific energy.
/// </summary>
public class GpParameters
{
    public double EnergyMeV { get; set; }
    public double A { get; set; }
    public double B { get; set; }
    public double C { get; set; }
    public double D { get; set; }
    public double Xk { get; set; }
}