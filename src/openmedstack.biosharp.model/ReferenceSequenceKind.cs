namespace OpenMedStack.BioSharp.Model
{
    public enum ReferenceSequenceKind : byte
    {
        LinearGenomic,
        CircularGenomic,
        Mitochondrial,
        CodingDna,
        NonCodingDna,
        Rna,
        Protein
        /*
        g.	= linear genomic reference sequence
        o.	= circular genomic reference sequence
        m.	= mitochondrial reference (special case of a circular genomic reference sequence)
        c.	= coding DNA reference sequence (based on a protein coding transcript)
        n.	= non-coding DNA reference sequence (based on a transcript not coding for a protein)

        RNA

        r.	= RNA reference sequence

        protein

        p.	= protein reference sequence

            */
    }
}