using System.Linq;
using Xunit;

namespace OpenMedStack.BioSharp.Io.Tests.Bam;

public class EncodingTests
{
    [Fact]
    public void CanRoundTripEncodeCigarString()
    {
        var cigar = new[] { (3u, 'M'), (2u, 'D') };
        var encoded = cigar.Select(x => x.Encode()).ToArray();
        var decoded = encoded.Select(x => x.Decode()).ToArray();

        Assert.Equal(cigar, decoded);
    }

    [Fact]
    public void CanRoundTripEncodeSequenceString()
    {
        var sequence = "ACMGRSVAATHKDBN";
        var encoded = sequence.WriteSequence();
        var decoded = encoded.ReadSequence();

        Assert.Equal(sequence, decoded);
    }
}
