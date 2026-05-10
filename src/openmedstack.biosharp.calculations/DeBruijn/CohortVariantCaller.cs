namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Represents a variant detected in a cohort sample with its frequency across the cohort.
/// </summary>
public class CohortVariant
{
    /// <summary>Position in reference (1-based).</summary>
    public int Position { get; set; }

    /// <summary>Reference allele.</summary>
    public string Reference { get; set; } = null!;

    /// <summary>Alternative (variant) allele.</summary>
    public string Alternate { get; set; } = null!;

    /// <summary>Number of samples in the cohort where this variant was detected.</summary>
    public int SampleCount { get; set; }

    /// <summary>Total number of samples in the cohort.</summary>
    public int TotalSamples { get; set; }

    /// <summary>
    /// Fraction of samples carrying this variant (0.0 - 1.0).
    /// </summary>
    public double AlleleFrequency
    {
        get
        {
            return TotalSamples > 0
                ? (double)SampleCount / TotalSamples
                : 0.0;
        }
    }

    /// <summary>
    /// Variant type classification based on cohort frequency.
    /// </summary>
    public CohortVariantType Type
    {
        get
        {
            if (SampleCount == 1)
            {
                return CohortVariantType.SingleSample; // Only in one sample
            }

            if (AlleleFrequency >= 0.9)
            {
                return CohortVariantType.Oncogenic; // Present in most samples
            }

            if (AlleleFrequency >= 0.5)
            {
                return CohortVariantType.Present; // Present in majority
            }

            return CohortVariantType.Subclonal; // Present in minority
        }
    }

    /// <summary>Average quality score across samples.</summary>
    public double AverageQuality { get; set; }

    /// <summary>Average allele fraction across samples.</summary>
    public double AverageAlleleFraction { get; set; }

    /// <summary>Names of samples where this variant was detected.</summary>
    public IList<string> DetectedIn { get; set; } = new List<string>();

    public override string ToString()
    {
        return $"Cohort[{Position}: {Reference}->{Alternate}] " +
            $"in {SampleCount}/{TotalSamples} samples ({Type}) Q={AverageQuality:F0}";
    }
}

/// <summary>
/// Classifies cohort variant frequency.
/// </summary>
public enum CohortVariantType
{
    /// <summary>Present in a single sample only.</summary>
    SingleSample,

    /// <summary>Present in some but most samples (subclonal).</summary>
    Subclonal,

    /// <summary>Present in roughly half or more of samples.</summary>
    Present,

    /// <summary>Pan-cohort variant, present in nearly all samples.</summary>
    Oncogenic
}

/// <summary>
/// Aggregates somatic/germline variants across a cohort of tumor samples
/// against a single normal Bloom filter.
/// </summary>
public static class CohortVariantCaller
{
    /// <summary>
    /// Minimum frequency at which a variant is classified as "Oncogenic" (present in most samples).
    /// </summary>
    public const double DefaultOncogenicThreshold = 0.9;

    /// <summary>
    /// Minimum frequency for "Subclonal" classification.
    /// </summary>
    public const double DefaultSubclonalThreshold = 0.2;

    /// <summary>
    /// Minimum quality threshold to include a variant in cohort results.
    /// Variants below this quality are excluded from all aggregations.
    /// </summary>
    public const int DefaultMinVariantQuality = 10;

    /// <summary>
    /// Processes a cohort of tumor samples and calls variants.
    /// Each tumor is compared against the same normal Bloom filter.
    /// Variants detected in multiple samples are aggregated and classified.
    /// </summary>
    /// <param name="tumorNormalPairs">
    /// List of (tumorName, tumorGraph) pairs. All share the same normal.
    /// </param>
    /// <param name="normalFilter">Bloom filter built from normal-sample k-mers.</param>
    /// <param name="reference">Reference sequence for position alignment.</param>
    /// <param name="chromosome">Chromosome name.</param>
    /// <param name="refStart">Reference start position.</param>
    /// <param name="minAlleleFraction">Minimum alt allele fraction per sample (default 0.20).</param>
    /// <returns>Aggregated cohort variants.</returns>
    public static async Task<IList<CohortVariant>> CallCohortVariantsAsync(
        IEnumerable<(string name, DeBruijnGraph tumorGraph)> tumorNormalPairs,
        BloomFilter normalFilter,
        string reference,
        string chromosome,
        int refStart,
        double? minAlleleFraction = null)
    {
        if (tumorNormalPairs == null)
        {
            throw new ArgumentNullException(nameof(tumorNormalPairs));
        }

        if (normalFilter == null)
        {
            throw new ArgumentNullException(nameof(normalFilter));
        }

        if (string.IsNullOrEmpty(reference))
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (string.IsNullOrEmpty(chromosome))
        {
            throw new ArgumentNullException(nameof(chromosome));
        }

        var minAlleleFrac = minAlleleFraction ?? 0.20;
        var minQuality = DefaultMinVariantQuality;

        // Step 1: Call somatic variants in each sample
        var sampleVariants = new Dictionary<string, IList<SomaticVariant>>();
        var totalSamples = tumorNormalPairs.Count();

        foreach (var (name, tumorGraph) in tumorNormalPairs)
        {
            var variants = await SomaticVariantDetector.DetectSomaticVariantsAsync(
                tumorGraph, normalFilter, reference, chromosome, refStart, minAlleleFrac, minQuality);
            sampleVariants[name] = variants;
        }

        // Step 2: Aggregate variants across samples
        // Group by (position, ref, alt)
        var allVariants = sampleVariants
            .SelectMany(sv => sv.Value.Select(v => (sample: sv.Key, variant: v)))
            .GroupBy(x => (x.variant.Position, x.variant.Reference, x.variant.Alternate))
            .ToList();

        var cohortVariants = new List<CohortVariant>();

        foreach (var group in allVariants)
        {
            var key = group.Key;
            var detections = group.ToList();
            var detectionSamples = detections.Select(d => d.sample).Distinct().ToList();
            var sampleCount = detectionSamples.Count;

            // Minimum 2 samples to be considered a coherent cohort variant
            // (use 1 for single-sample detection)
            if (sampleCount < 1)
            {
                continue;
            }

            var avgQuality = detections.Average(d => d.variant.Quality);

            // Filter by quality threshold
            if (avgQuality < minQuality && detections.Count == 1)
            {
                continue; // single sample with low quality -> skip
            }

            var avgMaf = detections.Average(d => d.variant.TumorAlleleFraction);

            cohortVariants.Add(new CohortVariant
            {
                Position = key.Position,
                Reference = key.Reference,
                Alternate = key.Alternate,
                SampleCount = sampleCount,
                TotalSamples = totalSamples,
                DetectedIn = detectionSamples,
                AverageQuality = avgQuality,
                AverageAlleleFraction = avgMaf
            });
        }

        cohortVariants.Sort((a, b) =>
        {
            var cmp = a.Position.CompareTo(b.Position);
            if (cmp != 0)
            {
                return cmp;
            }

            return b.SampleCount.CompareTo(a.SampleCount); // high frequency first
        });

        return cohortVariants;
    }

    /// <summary>
    /// Filters cohort variants by frequency threshold.
    /// </summary>
    /// <param name="variants">Raw cohort variants.</param>
    /// <param name="minFrequency">Minimum allele frequency to include (0.0 - 1.0).</param>
    /// <returns>Filtered variants.</returns>
    public static IList<CohortVariant> FilterByFrequency(
        IList<CohortVariant> variants,
        double minFrequency)
    {
        if (variants == null)
        {
            throw new ArgumentNullException(nameof(variants));
        }

        return variants.Where(v => v.AlleleFrequency >= minFrequency).ToList();
    }

    /// <summary>
    /// Filters cohort variants by variant type.
    /// </summary>
    /// <param name="variants">Raw or filtered cohort variants.</param>
    /// <param name="type">Variant type to filter for.</param>
    /// <returns>Filtered variants of the specified type.</returns>
    public static IList<CohortVariant> FilterByType(
        IList<CohortVariant> variants,
        CohortVariantType type)
    {
        if (variants == null)
        {
            throw new ArgumentNullException(nameof(variants));
        }

        return variants.Where(v => v.Type == type).ToList();
    }

    /// <summary>
    /// Gets summary statistics for a cohort variant call.
    /// </summary>
    public static CohortSummary GetSummary(IList<CohortVariant> variants, int totalSamples)
    {
        if (variants == null)
        {
            throw new ArgumentNullException(nameof(variants));
        }

        var totalVariants = variants.Count;
        var variantsInAllSamples = variants.Count(v => v.SampleCount >= totalSamples);
        var variantsInManySamples = variants.Count(v => v.SampleCount >= totalSamples * 0.5);
        var variantsInFewSamples = variants.Count(v => v.SampleCount < totalSamples * 0.5 && v.SampleCount > 0);
        var singleSample = variants.Count(v => v.SampleCount == 1);

        var avgQuality = variants.Any() ? variants.Average(v => v.AverageQuality) : 0.0;
        var avgMaf = variants.Any() ? variants.Average(v => v.AverageAlleleFraction) : 0.0;

        return new CohortSummary
        {
            TotalVariants = totalVariants,
            OncogenicCount = variants.Count(v => v.Type == CohortVariantType.Oncogenic),
            PresentCount = variants.Count(v => v.Type == CohortVariantType.Present),
            SubclonalCount = variants.Count(v => v.Type == CohortVariantType.Subclonal),
            SingleSampleCount = singleSample,
            AverageQuality = avgQuality,
            AverageAlleleFrequency = avgMaf
        };
    }
}

/// <summary>
/// Summary statistics for a cohort variant call result.
/// </summary>
public class CohortSummary
{
    public int TotalVariants { get; set; }
    public int OncogenicCount { get; set; }
    public int PresentCount { get; set; }
    public int SubclonalCount { get; set; }
    public int SingleSampleCount { get; set; }
    public double AverageQuality { get; set; }
    public double AverageAlleleFrequency { get; set; }

    public override string ToString()
    {
        return $"CohortSummary: {TotalVariants} variants, " +
            $"AvgQuality={AverageQuality:F1}, AvgFreq={AverageAlleleFrequency:F2}. " +
            $"Single={SingleSampleCount}, Oncogenic={OncogenicCount}, " +
            $"Present={PresentCount}, Subclonal={SubclonalCount}";
    }
}
