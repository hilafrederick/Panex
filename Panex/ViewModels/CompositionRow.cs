namespace Panex.ViewModels;

public class CompositionRow
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Z { get; set; }
    public double AtomicMass { get; set; }
    public double AtomFraction { get; set; }
    public double MassFraction { get; set; }

    public string AtomicMassString => AtomicMass.ToString("F6");
    public string AtomFractionString => AtomFraction.ToString("P4");
    public string MassFractionString => MassFraction.ToString("P4");
}