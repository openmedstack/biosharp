namespace OpenMedStack.BioSharp.Calculations.Alignment;

using Model;

/// <summary>
/// Abstraction over reference-seeding strategies.
///
/// Both the hash-map based <see cref="ReferenceIndex"/> and the FM-index
/// based <see cref="BurrowsWheeler.FmIndexSeeder"/> implement this interface,
/// allowing <see cref="VariantCallingPipeline"/> to use either back-end.
/// </summary>
public interface IReferenceSeeder
{
    /// <summary>Identifier of the underlying reference sequence.</summary>
    string ReferenceId { get; }

    /// <summary>Length of the underlying reference sequence in base pairs.</summary>
    int ReferenceLength { get; }

    /// <summary>
    /// Returns the set of candidate reference windows that the read should
    /// be aligned against (Smith-Waterman extension step).
    ///
    /// Implementations return windows sorted by descending seed-hit count so
    /// the first window is the most likely mapping location.
    /// </summary>
    ReferenceIndex.CandidateWindow[] FindCandidateWindows(Sequence read);
}

