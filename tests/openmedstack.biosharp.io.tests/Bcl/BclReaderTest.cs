namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Io.Bcl;
    using Microsoft.Extensions.Logging.Abstractions;
    using Xunit;

    public class BclReaderTest
    {
        private const string TestDataDir = "data/illumina/readerTests";
        private static readonly string PassingBclFile = Path.Combine(TestDataDir, "bcl_passing.bcl");
        private static readonly string Qual0FailingBclFile = Path.Combine(TestDataDir, "bcl_failing.bcl");
        private static readonly string Qual1FailingBclFile = Path.Combine(TestDataDir, "bcl_failing2.bcl");
        private static readonly string FileTooLong = Path.Combine(TestDataDir, "bcl_tooLong.bcl");
        private static readonly string FileTooShort = Path.Combine(TestDataDir, "bcl_tooShort.bcl");

        private static readonly char[] ExpectedBases =
            "CAAATCTGTAAGCCAACACCAACGATACAACATGCACAACGCAAGTGCACGTACAACGCACATTTAAGCGTCATGAGCTCTACGAACCCATATGGGCTGAANNGACCGTACAGTGTAN"
                .ToCharArray();

        private static readonly int[] ExpectedQuals = {
            18, 29, 8, 17, 27, 25, 28, 27, 9, 29, 8, 20, 25, 24, 27, 27,
            30, 8, 19, 24, 29, 29, 25, 28, 8, 29, 26, 24, 29, 8, 18, 8,
            29, 28, 26, 29, 25, 8, 26, 25, 28, 25, 8, 28, 28, 27, 29, 26,
            25, 26, 27, 25, 8, 18, 8, 26, 24, 29, 25, 8, 24, 8, 25, 27,
            27, 25, 8, 28, 24, 27, 25, 25, 8, 27, 25, 8, 16, 24, 28, 25,
            28, 8, 24, 27, 25, 8, 20, 29, 24, 27, 28, 8, 23, 10, 23, 11,
            15, 11, 10, 12, 12, 2, 2, 31, 24, 8, 4, 36, 12, 17, 21, 4,
            8, 12, 18, 23, 27, 2
    };

        private static char[] QualsAsBytes()
        {
            var byteVals = new char[ExpectedQuals.Length];
            for (var i = 0; i < byteVals.Length; i++)
            {
                byteVals[i] = (char)ExpectedQuals[i];
            }
            return byteVals;
        }

        [Fact]
        public async Task ReadValidFileInfo()
        {
            var bclQualityEvaluationStrategy =
                new BclQualityEvaluationStrategy(BclQualityEvaluationStrategy.IlluminaAllegedMinimumQuality);
            var reader = await BclReader.Create(
                new FileInfo(PassingBclFile),
                new TileIndexRecord(1, int.MaxValue, 0, 0),
                bclQualityEvaluationStrategy,
                NullLogger.Instance);
            var quals = QualsAsBytes();

            Assert.Equal(reader.NumClustersPerCycle[0], ExpectedBases.Length);

            var enumerator = reader.GetAsyncEnumerator();
            for (var readNum = 0; readNum < reader.NumClustersPerCycle[0]; readNum++)
            {
                await enumerator.MoveNextAsync();
                var bv = enumerator.Current;
                Assert.Equal(ExpectedBases[readNum], (char)bv[0].Bases.Span[0]); //" On num cluster: " + readNum);
                Assert.Equal(quals[readNum], bv[0].Qualities.Span[0]); //" On num cluster: " + readNum);
            }

            bclQualityEvaluationStrategy.AssertMinimumQualities();
            await reader.DisposeAsync();
        }

        public static object[][] FailingFiles()
        {
            return new[]
            {
                new object[] { Qual0FailingBclFile },
                new object[] { Qual1FailingBclFile },
                new object[] { Path.Combine(TestDataDir, "SomeNoneExistentFile.bcl") },
                new object[] { FileTooLong },
                new object[] { FileTooShort }
            };
        }

        [Theory]
        [MemberData(nameof(FailingFiles))]
        public async Task FailingFileTest(string failingFile)
        {
            _ = await Assert.ThrowsAnyAsync<Exception>(
                    async () =>
                    {
                        var bclQualityEvaluationStrategy =
                            new BclQualityEvaluationStrategy(
                                BclQualityEvaluationStrategy.IlluminaAllegedMinimumQuality);
                        var reader = await BclReader.Create(
                            new FileInfo(failingFile),
                            new TileIndexRecord(1, int.MaxValue, 0, 0),
                            bclQualityEvaluationStrategy,
                            NullLogger.Instance);
                        Assert.Equal(reader.NumClustersPerCycle[0], ExpectedBases.Length);

                        // Just loop through the data
                        _ = await reader.CountAsync();

                        await reader.DisposeAsync();
                        bclQualityEvaluationStrategy.AssertMinimumQualities();
                    });
        }

        /**
         * Asserts appropriate functionality of a quality-minimum-customized BLC reader, such that (1) if sub-Q2 qualities are found, the BCL
         * reader does not throw an exception, (2) sub-minimum calls are set to quality 1 and (3) sub-minimum calls are counted up properly.
         */
        [Fact]
        public async Task LowQualityButPassingTest()
        {
            var bclQualityEvaluationStrategy = new BclQualityEvaluationStrategy(1);

            for (var i = 0; i < 10; i++)
            {
                var evenI = i % 2 == 0;
                var reader = await BclReader.Create(
                    new FileInfo(evenI ? Qual1FailingBclFile : Qual0FailingBclFile),
                    new TileIndexRecord(1, int.MaxValue, 0, 0),
                    bclQualityEvaluationStrategy,
                    NullLogger.Instance);
                Assert.Equal(reader.NumClustersPerCycle[0], ExpectedBases.Length);

                // Just loop through the data
                _ = await reader.CountAsync();

                await reader.DisposeAsync();
            }

            bclQualityEvaluationStrategy.AssertMinimumQualities();
            Assert.Equal(25, bclQualityEvaluationStrategy.GetPoorQualityFrequencies()[0]);
            Assert.Equal(25, bclQualityEvaluationStrategy.GetPoorQualityFrequencies()[1]);
        }

        [Fact]
        public async Task LowQualityAndFailingTest()
        {
            var bclQualityEvaluationStrategy = new BclQualityEvaluationStrategy(BclQualityEvaluationStrategy.IlluminaAllegedMinimumQuality);

            // Build a list of tasks, then submit them and check for errors.
            for (var i = 0; i < 10; i++)
            {
                var reader = await BclReader.Create(
                    new FileInfo(i % 2 == 0 ? Qual1FailingBclFile : Qual0FailingBclFile),
                    new TileIndexRecord(1, int.MaxValue, 0, 0),
                    bclQualityEvaluationStrategy,
                    NullLogger.Instance);
                Assert.Equal(ExpectedBases.Length, reader.NumClustersPerCycle[0]);

                // Just loop through the data
                _ = await reader.CountAsync();

                await reader.DisposeAsync();
            }

            Assert.Equal(25, bclQualityEvaluationStrategy.GetPoorQualityFrequencies()[0]);
            Assert.Equal(25, bclQualityEvaluationStrategy.GetPoorQualityFrequencies()[1]);
            Assert.Throws<Exception>(bclQualityEvaluationStrategy.AssertMinimumQualities);
        }
    }
}
