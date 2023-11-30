namespace OpenMedStack.BioSharp.Io.Tests.Bam;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.Bam;
using Xunit;

public class BamWriterTests
{
    [Fact]
    public async Task CanWrite()
    {
        var reader = new BamReader("mapt.NA12156.altex.bam",NullLogger<BamReader>.Instance);
        var result = await reader.Read( CancellationToken.None);

        var alignmentCount = result.AlignmentSections.Length;
        await using (var output = new BgzfStream(File.Open("bam_test.bam", FileMode.Create, FileAccess.Write),
            System.IO.Compression.CompressionLevel.SmallestSize, false))
        {
            var writer = new BamWriter(output, NullLogger<BamWriter>.Instance);
            await writer.Write(result, CancellationToken.None);
        }

        reader = new BamReader("bam_test.bam", NullLogger<BamReader>.Instance);
        var readBack = await reader.Read( CancellationToken.None);

        Assert.Equal(alignmentCount, readBack.AlignmentSections.Length);
    }
}
