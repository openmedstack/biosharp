using System;
using System.Diagnostics.CodeAnalysis;

namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Defines the HGVS Variant type.
/// </summary>
public record HgvsVariant
{
    private HgvsVariant(string reference, int version, HgvsDescription description)
    {
        Reference = reference;
        Version = version;
        Description = description;
    }

    public string Reference { get; }

    public int Version { get; }

    public HgvsDescription Description { get; }

    public static bool TryParse(string input, [NotNullWhen(true)] out HgvsVariant? result)
    {
        try
        {
            result = Parse(input);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    public static HgvsVariant Parse(string input)
    {
        var colon = input.IndexOf(':');
        var dot = input.IndexOf('.');
        // Only treat the dot as a version separator when it appears before the colon
        // (e.g. NM_004006.2:c.4375C>T). If the dot is after the colon it is part of
        // the description (e.g. BRCA1:c.100A>G) and there is no version number.
        var hasVersionDot = dot >= 0 && dot < colon;
        var reference = hasVersionDot ? input[..dot] : input[..colon];
        var version = 0;
        var hasVersion = hasVersionDot && int.TryParse(input.AsSpan(dot + 1, colon - dot - 1), out version);
        var description = HgvsDescription.Parse(input[(colon + 1)..]);

        return new HgvsVariant(reference, hasVersion ? version : 0, description);
    }
}
