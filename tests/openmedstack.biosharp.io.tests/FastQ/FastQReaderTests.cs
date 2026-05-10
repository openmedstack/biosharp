namespace OpenMedStack.BioSharp.Io.Tests.FastQ;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Io.FastQ;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class FastQReaderTests
{
    private const string FastQerr = "ERR164409.fastq.gz";
    private readonly ITestOutputHelper _outputHelper;

    public FastQReaderTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task CanCreateSequence()
    {
        var parser = new FastQReader(NullLogger.Instance);
        await foreach (var sequence in parser.Read(FastQerr, TestContext.Current.CancellationToken))
        {
            Assert.NotEmpty(sequence);
        }
    }

    [Fact]
    public async Task CanConvertToString()
    {
        var parser = new FastQReader(NullLogger.Instance);
        var sequence = await parser.Read(FastQerr, TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(sequence);

        _outputHelper.WriteLine(sequence.ToString());
    }

    [Fact]
    public async Task CanWrite()
    {
        var output = new MemoryStream();
        var reader = new FastQReader(NullLogger.Instance);
        var writer = new FastQWriter(new NullLogger<FastQWriter>(), output, Stream.Null);

        var sequence = await reader.Read(FastQerr, TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);
        await writer.Write(sequence, TestContext.Current.CancellationToken);

        Assert.True(output.Length > 0);

        await output.DisposeAsync();
    }

    [Fact]
    public async Task CanReadPlainFastQ()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPath, "@read1\nACGT\n+\nIIII\n", TestContext.Current.CancellationToken);
            var parser = new FastQReader(NullLogger.Instance);

            var sequences = new System.Collections.Generic.List<Model.Sequence>();
            await foreach (var sequence in parser.Read(tempPath, TestContext.Current.CancellationToken))
            {
                sequences.Add(sequence);
            }

            Assert.Single(sequences);
            Assert.Equal("read1", sequences[0].Id);
            Assert.Equal("ACGT", sequences[0].GetData().Span.ToString());
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
