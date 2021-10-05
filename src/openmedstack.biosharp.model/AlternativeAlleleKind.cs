namespace OpenMedStack.BioSharp.Model
{
    public enum AlternativeAlleleKind : byte
    {
        /// <summary>
        /// Deletion relative to the reference
        /// </summary>
        Del,
        /// <summary>
        /// Deletion of mobile element relative to the reference
        /// </summary>
        DelMe,
        /// <summary>
        /// Insertion of novel sequence relative to the reference
        /// </summary>
        Ins,
        /// <summary>
        /// Insertion of a mobile element relative to the reference
        /// </summary>
        InsMe,
        /// <summary>
        /// Region of elevated copy number relative to the reference
        /// </summary>
        Dup,
        /// <summary>
        /// Tandem duplication
        /// </summary>
        DupTandem,
        /// <summary>
        /// Inversion of reference sequence
        /// </summary>
        Inv,
        /// <summary>
        /// Copy number variable region (may be both deletion and duplication). The CNV category should not be used when a more specific category can be applied.
        /// </summary>
        Cnv
    }
}