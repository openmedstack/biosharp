namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A group of alignment events that form a single variant call.
/// Used for multi-base events (e.g., two consecutive SNPs or an insertion followed by a SNP).
/// </summary>
public class VariantGroup
{
    /// <summary>Relative position in the aligned sequence where the group starts.</summary>
    public int StartIndex { get; set; }

    /// <summary>All alignment events in this group.</summary>
    public List<AlignmentEvent> Events { get; } = [];

    /// <summary>First event type in the group (used for grouping decision).</summary>
    public EventType FirstEventType
    {
        get { return Events.Count > 0 ? Events[0].EventType : EventType.Snp; }
    }

    /// <summary>True if any event is an insertion or deletion.</summary>
    public bool HasIndel
    {
        get { return Events.Any(e => e.IsIndel); }
    }

    public void Add(AlignmentEvent evt)
    {
        Events.Add(evt);
    }
}
