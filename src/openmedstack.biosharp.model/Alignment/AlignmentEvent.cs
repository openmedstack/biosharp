namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// A single alignment event: a SNP, insertion, or deletion at a specific position.
/// </summary>
public class AlignmentEvent
{
    /// <summary>Type of variant event.</summary>
    public EventType EventType { get; }

    /// <summary>0-based position in the aligned sequence.</summary>
    public int Position { get; }

    /// <summary>The base from the read (for SNP and insertion).</summary>
    public char Base { get; }

    public AlignmentEvent(EventType eventType, int position, char baseChar)
    {
        EventType = eventType;
        Position = position;
        Base = baseChar;
    }

    /// <summary>
    /// Length of homopolymer run at this event's position in the reference.
    /// Used for quality scoring: homopolymer indels are downweighted.
    /// </summary>
    public int HomopolymerRun { get; internal set; }

    /// <summary>True if this is a substitution/SNP.</summary>
    public bool IsSubstitution
    {
        get { return EventType == EventType.Snp; }
    }

    /// <summary>True if this is an indel (insertion or deletion).</summary>
    public bool IsIndel
    {
        get { return EventType is EventType.Insertion or EventType.Deletion; }
    }
}
