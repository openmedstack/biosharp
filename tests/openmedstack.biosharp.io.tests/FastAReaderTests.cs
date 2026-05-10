namespace OpenMedStack.BioSharp.Io.Tests;

using System.Linq;
using System.Threading.Tasks;
using FastA;
using Xunit;

public class FastAReaderTests
{
    [Theory]
    [InlineData("chr1.fa.gz")]
    public async Task CanReadSingleSequence(string path)
    {
        var parser = new FastAReader();
        var sequence = await parser.ReadGz(path, cancellationToken: TestContext.Current.CancellationToken)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(sequence);
    }
}
