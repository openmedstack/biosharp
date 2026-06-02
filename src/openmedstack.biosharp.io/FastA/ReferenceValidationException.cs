using System;

namespace OpenMedStack.BioSharp.Io.FastA;

/// <summary>
/// Exception thrown when a reference FASTA fails checksum validation.
/// </summary>
public sealed class ReferenceValidationException : Exception
{
    public ReferenceValidationException(string message) : base(message) { }
}