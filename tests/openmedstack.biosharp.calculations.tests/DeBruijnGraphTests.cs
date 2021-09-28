namespace OpenMedStack.BioSharp.Calculations.Tests
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DeBruijn;
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
                        r => new Sequence(Guid.NewGuid().ToString(), Encoding.ASCII.GetBytes(r), new byte[r.Length]))
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
                        r => new Sequence(Guid.NewGuid().ToString(), Encoding.ASCII.GetBytes(r), new byte[r.Length]))
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
    }
}