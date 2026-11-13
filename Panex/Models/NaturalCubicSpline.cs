// File: \Models\NaturalCubicSpline.cs
using System;

namespace Panex.Models;

public class NaturalCubicSpline
{
    private readonly double[] x;
    private readonly double[] a, b, c, d;
    private readonly int n;

    public NaturalCubicSpline(double[] x, double[] y)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("x and y must have the same length.");
        if (x.Length < 2)
            throw new ArgumentException("At least two points are required.");

        n = x.Length;
        this.x = x;
        a = new double[n];
        b = new double[n - 1];
        c = new double[n];
        d = new double[n - 1];

        Array.Copy(y, a, n);
        ComputeSpline();
    }

    private void ComputeSpline()
    {
        double[] h = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            h[i] = x[i + 1] - x[i];
            if (h[i] <= 0)
                throw new ArgumentException("x values must be strictly increasing.");
        }

        double[] alpha = new double[n - 1];
        for (int i = 1; i < n - 1; i++)
        {
            alpha[i] = (3.0 / h[i]) * (a[i + 1] - a[i]) - (3.0 / h[i - 1]) * (a[i] - a[i - 1]);
        }

        double[] l = new double[n];
        double[] mu = new double[n];
        double[] z = new double[n];

        // Natural boundary conditions
        l[0] = 1.0;
        mu[0] = 0.0;
        z[0] = 0.0;

        for (int i = 1; i < n - 1; i++)
        {
            l[i] = 2.0 * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1];
            mu[i] = h[i] / l[i];
            z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
        }

        l[n - 1] = 1.0;
        z[n - 1] = 0.0;
        c[n - 1] = 0.0;

        for (int j = n - 2; j >= 0; j--)
        {
            c[j] = z[j] - mu[j] * c[j + 1];
            b[j] = (a[j + 1] - a[j]) / h[j] - h[j] * (c[j + 1] + 2.0 * c[j]) / 3.0;
            d[j] = (c[j + 1] - c[j]) / (3.0 * h[j]);
        }
    }

    public double Evaluate(double xQuery)
    {
        // Clamp out-of-bounds queries to prevent floating-point drift crashes
        if (xQuery <= x[0]) return a[0];
        if (xQuery >= x[n - 1]) return a[n - 1];

        int i = FindSegment(xQuery);
        double dx = xQuery - x[i];
        return a[i] + b[i] * dx + c[i] * dx * dx + d[i] * dx * dx * dx;
    }

    private int FindSegment(double xQuery)
    {
        // Safe binary search for O(log N) lookups
        int low = 0, high = n - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (mid == n - 1) return mid - 1;
            if (xQuery >= x[mid] && xQuery <= x[mid + 1]) return mid;
            if (xQuery < x[mid]) high = mid - 1;
            else low = mid + 1;
        }

        return n - 2; // fallback
    }
}