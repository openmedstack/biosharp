namespace OpenMedStack.BioSharp.Io.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Calculations;
    using FastQ;
    using Model;
    using Xunit;

    public class SequenceExtensionsTests
    {
        [Theory]
        [InlineData("TAAT", 11)]
        [InlineData("TATTA", 1)]
        public async Task CanFindSubSequence(string code, int position)
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            Assert.Equal(position, sequence.IndexOf(code.ToCharArray()));
        }

        [Fact]
        public async Task WhenIndexOfMaxErrorsIsEqualToSequenceLengthThenIndexIsZero()
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            Assert.Equal(0, sequence.IndexOf("TACTAC".ToCharArray(), 6));
        }

        [Fact]
        public async Task WhenSequenceMatchesWithinAllowedErrorsThenFinds()
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            Assert.Equal(11, sequence.IndexOf("TAAX".ToCharArray(), 1));
        }

        [Fact]
        public async Task CanFindAllSubSequences()
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            var actual = sequence.AllIndicesOf("TAT".ToCharArray()).ToArray();
            Assert.Equal(new[] { 1, 50 }, actual);
        }

        [Fact]
        public async Task CanCountAllSubSequences()
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            var actual = sequence.Count("TAT".ToCharArray());
            Assert.Equal(2, actual);
        }

        [Fact]
        public async Task CanReadAminoSequenceToStop()
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            var actual = sequence.ToRna().ReadAminoAcids(true).ToArray();
            var expected = "VLVHNALW".Select<char, AminoAcid>(c => c).ToArray();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task CanReadAminoSequenceToEnd()
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            var actual = string.Join("", sequence.ToRna().ReadAminoAcids().Select(a => a.ToString()).ToArray());
            var expected = "VLVHNALW*TTTAGGIIIGSDDAKAHRE*";
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("UAA", 1)]
        [InlineData("UAG", 1)]
        [InlineData("UGA", 3)]
        public async Task CanCountAllStopCodons(string codon, int count)
        {
            const string path = "ERR164409.fastq.gz";
            var parser = new FastQReader();

            var sequence = await parser.Read(path).FirstAsync().ConfigureAwait(false);
            var actual = sequence.ToRna().Count(codon.ToCharArray());
            Assert.Equal(count, actual);
        }
    }
}