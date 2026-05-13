using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Calculations;

/// <summary>
/// Per-base nucleotide composition (% A/C/G/T/N) at a given cycle.
/// </summary>
public sealed class CycleComposition
{
    public double A { get; init; }
    public double C { get; init; }
    public double G { get; init; }
    public double T { get; init; }
    public double N { get; init; }

    /// <summary>Creates a lookup by character (A/C/G/T/N → percentage).</summary>
    public IReadOnlyDictionary<char, double> AsDict()
        => new Dictionary<char, double>
        {
            ['A'] = A, ['C'] = C, ['G'] = G, ['T'] = T, ['N'] = N
        };
}