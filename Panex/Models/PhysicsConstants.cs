using System;

namespace Panex.Models;

public static class PhysicsConstants
{
    public const double Avogadro = 6.02214076e23;
    public const double BarnsToCm2 = 1.0e-24;

    /// <summary>
    /// Interpolates a linear Y value based on a logarithmic X scale.
    /// Safely handles bounds to prevent NaN/Infinity.
    /// </summary>
    public static double InterpolateLinearOnLogX(double xTarget, double x1, double x2, double y1, double y2)
    {
        if (x1 <= 0 || x2 <= 0 || xTarget <= 0 || x1 == x2) return y1;

        double logX1 = Math.Log(x1);
        double logX2 = Math.Log(x2);
        double logXt = Math.Log(xTarget);

        if (logX1 == logX2) return y1;

        return y1 + (y2 - y1) * ((logXt - logX1) / (logX2 - logX1));
    }
}