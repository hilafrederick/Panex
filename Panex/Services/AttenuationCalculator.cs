// File: \Services\AttenuationCalculator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Panex.Models;

namespace Panex.Services;

public static class AttenuationCalculator
{
    public static (double[] LAC, double[] MFP, double[] NVL) CalculateMetrics(
        double[] energyGrid,
        IEnumerable<CrossSectionCurve> activeCurves,
        double density,
        double nValue,
        double maxThicknessLimit = 100.0) // Clamping value for the linear Y-axis
    {
        int length = energyGrid.Length;
        double[] lacValues = new double[length];
        double[] mfpValues = new double[length];
        double[] nvlValues = new double[length];

        for (int i = 0; i < length; i++)
        {
            double energy = energyGrid[i];
            double sumMac = 0.0;

            // Sum up only the active (checked) curves
            foreach (var curve in activeCurves)
            {
                sumMac += curve.Interpolate(energy);
            }

            // Calculate LAC (cm^-1)
            double lac = sumMac * density;
            lacValues[i] = lac;

            // Calculate MFP and n-th Value Layer (cm)
            if (lac > 0)
            {
                double mfp = 1.0 / lac;
                double nvl = Math.Log(nValue) / lac;

                // Clamp the values to prevent infinity from ruining the linear Y-axis
                mfpValues[i] = Math.Min(mfp, maxThicknessLimit);
                nvlValues[i] = Math.Min(nvl, maxThicknessLimit);
            }
            else
            {
                mfpValues[i] = maxThicknessLimit;
                nvlValues[i] = maxThicknessLimit;
            }
        }

        return (lacValues, mfpValues, nvlValues);
    }
}