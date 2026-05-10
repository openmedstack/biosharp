namespace OpenMedStack.BioSharp.Io.Tests.Bam;

using System.Threading;
using System.Threading.Tasks;
using Io.Bam;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class BamReaderTests
{
    [Theory]
    [InlineData("GSM409307_UCSD.H3K4me1.bam", 8038656)]
    [InlineData("mapt.NA12156.altex.bam", 326652)]
    public async Task CanRead(string filename, int expectedAlignments)
    {
        var reader = new BamReader(filename, NullLogger<BamReader>.Instance);
        var result = await reader.Read(CancellationToken.None);

        Assert.Equal(expectedAlignments, result.AlignmentSections.Length);
    }
}
