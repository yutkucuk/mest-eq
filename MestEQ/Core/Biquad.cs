using System;
using System.Collections.Generic;
using System.Numerics;

namespace MestEQ.Core
{
    /// <summary>
    /// RBJ Audio EQ Cookbook biquad'larının frekans yanıtı. Equalizer APO aynı formülleri kullanır;
    /// grafik ve preamp hesabı bu yüzden APO'nun gerçekte yaptığıyla örtüşür.
    /// </summary>
    public static class Biquad
    {
        public const double SampleRate = 48000.0;

        public readonly struct Coeffs
        {
            public readonly double B0, B1, B2, A0, A1, A2;
            public Coeffs(double b0, double b1, double b2, double a0, double a1, double a2)
            { B0 = b0; B1 = b1; B2 = b2; A0 = a0; A1 = a1; A2 = a2; }
        }

        public static Coeffs Design(FilterKind kind, double f0, double gainDb, double q, double fs = SampleRate)
        {
            double A = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * Math.Min(f0, fs * 0.49) / fs;
            double cos = Math.Cos(w0), sin = Math.Sin(w0);
            double alpha = sin / (2 * q);
            double sqA = Math.Sqrt(A);

            switch (kind)
            {
                case FilterKind.LowShelf:
                    return new Coeffs(
                        A * ((A + 1) - (A - 1) * cos + 2 * sqA * alpha),
                        2 * A * ((A - 1) - (A + 1) * cos),
                        A * ((A + 1) - (A - 1) * cos - 2 * sqA * alpha),
                        (A + 1) + (A - 1) * cos + 2 * sqA * alpha,
                        -2 * ((A - 1) + (A + 1) * cos),
                        (A + 1) + (A - 1) * cos - 2 * sqA * alpha);
                case FilterKind.HighShelf:
                    return new Coeffs(
                        A * ((A + 1) + (A - 1) * cos + 2 * sqA * alpha),
                        -2 * A * ((A - 1) + (A + 1) * cos),
                        A * ((A + 1) + (A - 1) * cos - 2 * sqA * alpha),
                        (A + 1) - (A - 1) * cos + 2 * sqA * alpha,
                        2 * ((A - 1) - (A + 1) * cos),
                        (A + 1) - (A - 1) * cos - 2 * sqA * alpha);
                default:
                    return new Coeffs(
                        1 + alpha * A, -2 * cos, 1 - alpha * A,
                        1 + alpha / A, -2 * cos, 1 - alpha / A);
            }
        }

        public static double MagnitudeDb(in Coeffs c, double f, double fs = SampleRate)
        {
            double w = 2 * Math.PI * f / fs;
            var z1 = Complex.FromPolarCoordinates(1, -w);
            var z2 = z1 * z1;
            var num = c.B0 + c.B1 * z1 + c.B2 * z2;
            var den = c.A0 + c.A1 * z1 + c.A2 * z2;
            double mag = (num / den).Magnitude;
            return 20 * Math.Log10(Math.Max(mag, 1e-12));
        }

        /// <summary>20 Hz – 20 kHz arası logaritmik frekans ızgarası.</summary>
        public static double[] LogGrid(int points = 480, double fMin = 20, double fMax = 20000)
        {
            var g = new double[points];
            double lmin = Math.Log10(fMin), lmax = Math.Log10(fMax);
            for (int i = 0; i < points; i++) g[i] = Math.Pow(10, lmin + (lmax - lmin) * i / (points - 1));
            return g;
        }

        public static double[] Response(IEnumerable<EqFilter> filters, double[] grid)
        {
            var r = new double[grid.Length];
            foreach (var f in filters)
            {
                if (!f.Enabled || Math.Abs(f.Gain) < 1e-9) continue;
                var c = Design(f.Kind, f.Freq, f.Gain, f.Q);
                for (int i = 0; i < grid.Length; i++) r[i] += MagnitudeDb(c, grid[i]);
            }
            return r;
        }
    }
}
