using OpenMedStack.BioSharp.Calculations.Alignment;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Result of structural variant analysis - bubbles, tips, and variant calls.
/// </summary>
public class StructuralVariantAnalysis
{
    public StructuralVariantAnalysis(LocalVariantResult[] variants)
    {
        Variants = variants;
    }

    public LocalVariantResult[] Variants { get; }
}