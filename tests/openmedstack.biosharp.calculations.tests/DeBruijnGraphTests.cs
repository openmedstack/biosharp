namespace OpenMedStack.BioSharp.Calculations.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DeBruijn;
    using Divergic.Logging.Xunit;
    using Io.FastQ;
    using Model;
    using Xunit;
    using Xunit.Abstractions;

    public class DeBruijnGraphTests
    {
        private readonly ITestOutputHelper _output;

        public DeBruijnGraphTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(7)]
        [InlineData(10)]
        public async Task CanCreateGraphFromShortText(int k)
        {
            var rnd = new Random(DateTime.UtcNow.Millisecond);
            const string resultSeq = "GCACACTAGTGCACTTCAGTAGGCTAGGAAGAAACTCCCTTGTGGTCAATG";
            var reads = Enumerable.Repeat(true, 10000).Select(_ =>
            {
                var readLength = rnd.Next(k, resultSeq.Length / 3);
                return resultSeq.Substring(rnd.Next(resultSeq.Length - readLength + 1), readLength);
            });
            var graph = new DeBruijnGraph(
                k,
                reads.Select(
                        r => new Sequence(
                            Guid.NewGuid().ToString(), r.AsMemory(), new char[r.Length]))
                    .ToAsyncEnumerable());
            var sequencesFound = 0;
            await foreach (var output in graph.Assemble(CancellationToken.None).ConfigureAwait(false))
            {
                _output.WriteLine(output);
                if (output == resultSeq)
                {
                    sequencesFound++;
                }
            }

            Assert.True(sequencesFound > 0);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(150)]
        [InlineData(70)]
        [InlineData(50)]
        [InlineData(30)]
        public async Task CanCreateGraphFromLongText(int k)
        {
            var rnd = new Random(DateTime.UtcNow.Millisecond);
            const string resultSeq =
                "It was the best of times, it was the worst of times, it was the age of wisdom, it was the age of foolishness, it was the epoch of belief, it was the epoch of incredulity, it was the season of Life, it was the season of Darkness, it was the spring of hope, it was the winter of despair, we had everything before us, we had nothing before us, we were all going direct to Heaven, we were all going direct the other way-in short, the period was so far the like present period, that some of its noisiest authorities insisted on its being received, for good or for evil, in the superlative degree of comparison only.";
            var reads = Enumerable.Repeat(true, 10_000).Select(_ =>
            {
                var readLength = rnd.Next(k, resultSeq.Length / 4);
                return resultSeq.Substring(rnd.Next(resultSeq.Length - readLength + 1), readLength);
            });
            var graph = new DeBruijnGraph(
                k,
                reads.Select(
                        r => new Sequence(Guid.NewGuid().ToString(), r.AsMemory(), new char[r.Length]))
                    .ToAsyncEnumerable());
            var sequencesFound = 0;
            await foreach (var output in graph.Assemble(CancellationToken.None).ConfigureAwait(false))
            {
                if (output == resultSeq)
                {
                    _output.WriteLine(output);
                    sequencesFound++;
                }
            }

            Assert.True(sequencesFound > 0);
        }

        [Fact]
        public async Task CanAssembleFromFiles()
        {
            var output = File.OpenWrite("output.log");
            await using var _ = output.ConfigureAwait(false);
            var writer = new StreamWriter(output, Encoding.UTF8);
            await using var __ = writer.ConfigureAwait(false);
            var reader = new FastQReader(new TestOutputLogger("test", _output));
            var graph = new DeBruijnGraph(
                100,
                reader.Read(
                    "..\\..\\..\\..\\..\\..\\200129_NB551214_0127_AH7CMYBGXF\\Unaligned\\200129_NB551214_0127_AH7CMYBGXF\\L001_TGGCTAGT_R002.fastq.gz"));
            await foreach (var seq in graph.Assemble(CancellationToken.None).ConfigureAwait(false))
            {
                await writer.WriteLineAsync(seq).ConfigureAwait(false);
            }
        }
    }
}