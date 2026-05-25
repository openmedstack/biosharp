using System;

namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Genotype call for a bubble variant, including caller name, zygosity,
/// and genotype quality derived from a binomial test on coverage ratios.
/// </summary>
public class Genotype
{
    /// <summary>The variant caller name (e.g. "DeBruijn").</summary>
    public string Caller { get; }

    /// <summary>The zygosity classification (0/0, 0/1, or 1/1).</summary>
    public GenotypeType Type { get; }

    /// <summary>Phred-scaled genotype quality (0–99).</summary>
    public int GQ { get; }

    /// <summary>The raw alt/ref coverage ratio used for classification.</summary>
    public double AltRefRatio { get; }

    /// <summary>Coverage on the reference allele.</summary>
    public int RefCoverage { get; }

    /// <summary>Coverage on the alternate allele.</summary>
    public int AltCoverage { get; }

    /// <summary>
    /// Creates a genotype call from ref and alt coverages.
    /// </summary>
    /// <param name="refCoverage">Coverage on the reference allele.</param>
    /// <param name="altCoverage">Coverage on the alternate allele.</param>
    public Genotype(int refCoverage, int altCoverage)
    {
        if (refCoverage < 0 || altCoverage < 0)
        {
            throw new ArgumentException("Coverage values must be non-negative.", nameof(refCoverage));
        }

        Caller = "DeBruijn";
        RefCoverage = refCoverage;
        AltCoverage = altCoverage;

        var total = refCoverage + altCoverage;

        if (total == 0)
        {
            Type = GenotypeType.HomozygousRef;
            GQ = 0;
            AltRefRatio = 0.0;
            return;
        }

        AltRefRatio = (double)altCoverage / refCoverage;

        if (refCoverage == 0 && altCoverage > 0)
        {
            Type = GenotypeType.HomozygousAlt;
            GQ = CalculateGQ(altCoverage, total);
        }
        else if (AltRefRatio >= 0.8)
        {
            Type = GenotypeType.HomozygousAlt;
            GQ = CalculateGQ(altCoverage, total);
        }
        else if (AltRefRatio >= 0.4)
        {
            Type = GenotypeType.Heterozygous;
            GQ = CalculateGQ(altCoverage, total);
        }
        else if (AltRefRatio < 0.2)
        {
            Type = GenotypeType.HomozygousRef;
            GQ = CalculateGQ(altCoverage, total);
        }
        else
        {
            // Borderline (0.2 – 0.4) — classify as homozygous ref as conservative default
            Type = GenotypeType.HomozygousRef;
            GQ = CalculateGQ(AltCoverage, total);
        }
    }

    /// <summary>
    /// Computes Phred-scaled genotype quality from a binomial CDF P-value.
    /// Under the heterozygous null hypothesis, we expect alt proportion ≈ 0.5.
    /// P = P(X ≥ altCoverage | n = total, p = 0.5) — the two-sided tail probability.
    /// GQ = -10 * log10(P), clamped to [0, 99].
    /// </summary>
    private static int CalculateGQ(int altCoverage, int total)
    {
        if (total == 0)
        {
            return 0;
        }

        const double p = 0.5;

        // Two-sided binomial CDF P-value:
        // P = P(X <= min(successes, n-successes)) + P(X >= max(successes, n-successes))
        // but capped at 1.0
        var lower = Math.Min(altCoverage, total - altCoverage);
        var upper = Math.Max(altCoverage, total - altCoverage);

        var pLower = BinomialCdf(lower, total, p);
        var pUpper = 1.0 - BinomialCdf(upper - 1, total, p);

        var pValue = Math.Min(pLower + pUpper, 1.0);

        if (pValue <= 0.0)
        {
            return 99;
        }

        var gq = -10.0 * Math.Log10(pValue);
        return Math.Clamp((int)Math.Round(gq), 0, 99);
    }

    /// <summary>
    /// Computes the cumulative distribution function of the binomial distribution:
    /// P(X ≤ k) where X ~ Binomial(n, p).
    /// Uses the regularized incomplete beta function approximation.
    /// </summary>
    private static double BinomialCdf(int k, int n, double p)
    {
        if (k < 0 || n <= 0 || p <= 0 || p >= 1)
        {
            if (k < 0)
            {
                return 0.0;
            }

            return 1.0;
        }

        // For small n, use direct summation
        if (n <= 200)
        {
            var sum = 0.0;
            for (var i = 0; i <= k; i++)
            {
                sum += BinomialPmf(i, n, p);
            }

            return sum;
        }

        // For large n, use the normal approximation
        var mean = n * p;
        var variance = n * p * (1.0 - p);
        if (variance < 1e-10)
        {
            return k >= Math.Round(mean) ? 1.0 : 0.0;
        }

        var z = (k + 0.5 - mean) / Math.Sqrt(variance);
        return NormalCDF(z);
    }

    /// <summary>
    /// Probability mass function: P(X = k) for X ~ Binomial(n, p).
    /// </summary>
    private static double BinomialPmf(int k, int n, double p)
    {
        if (k < 0 || k > n)
        {
            return 0.0;
        }

        return BinomialCoefficient(n, k) * Math.Pow(p, k) * Math.Pow(1.0 - p, n - k);
    }

    /// <summary>
    /// Computes binomial coefficient C(n, k) using a numerically stable iterative approach.
    /// </summary>
    private static double BinomialCoefficient(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return 0.0;
        }

        if (k == 0 || k == n)
        {
            return 1.0;
        }

        if (k > n / 2)
        {
            k = n - k;
        }

        var result = 1.0;
        for (var i = 0; i < k; i++)
        {
            result = result * (n - i) / (i + 1);
        }

        return result;
    }

    /// <summary>
    /// Approximation of the standard normal CDF using the Abramowitz & Stegun formula.
    /// </summary>
    private static double NormalCDF(double z)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var sign = z < 0 ? -1.0 : 1.0;
        z = Math.Abs(z) / Math.Sqrt(2.0);
        var t = 1.0 / (1.0 + p * z);
        var y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-z * z);
        return 0.5 * (1.0 + sign * y);
    }

    /// <summary>
    /// VCF GT string representation.
    /// </summary>
    public string ToVcfGenotype()
    {
        return Type switch
        {
            GenotypeType.HomozygousRef => "0/0",
            GenotypeType.Heterozygous => "0/1",
            GenotypeType.HomozygousAlt => "1/1",
            _ => "./*"
        };
    }

    public override string ToString()
    {
        return $"[genotype={ToVcfGenotype()}, GQ={GQ}, altCov={AltCoverage}, refCov={RefCoverage}, call=\"{Caller}\"]";
    }
}
