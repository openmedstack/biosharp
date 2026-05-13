namespace OpenMedStack.BioSharp.Io.Tests.Bam;

using System.Threading;
using System.Threading.Tasks;
using Io.Bam;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class BamReaderTests
{
    [Theory]
     [InlineData("small_test_sorted.bam", 62000)]
     [InlineData("mapt.NA12156.altex.bam", 326652)]
    public async Task CanRead(string filename, int expectedAlignments)
    {
        var reader = new BamReader(filename, NullLogger<BamReader>.Instance);
        var result = await reader.Read(CancellationToken.None);

        Assert.Equal(expectedAlignments, result.AlignmentSections.Length);
    }
}
