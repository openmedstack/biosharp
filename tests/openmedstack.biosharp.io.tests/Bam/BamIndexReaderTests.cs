namespace OpenMedStack.BioSharp.Io.Tests.Bam;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Io.Bam;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class BamIndexReaderTests
{
    private readonly BamIndexReader _reader = new(NullLogger.Instance);

    [Theory]
    [InlineData("mapt.NA12156.altex.bam.bai", 353)]
    public async Task CanRead(string filename, int expectedAlignments)
    {
        var result = await _reader.Read(filename, CancellationToken.None);

        Assert.Equal(expectedAlignments, result.Content.Sum(x => x.Content.Sum(b => b.Chunks.Length)));
    }
}
