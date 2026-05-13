using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class TestDataGeneratorTests
{
    [Fact]
    public void TestDataGenerator_GeneratesValidReferenceFasta()
    {
        var gen = new TestDataGenerator(seed: 42);
        var fasta = gen.GenerateReference(length: 1000, repeatFraction: 0.1);

        Assert.Equal(1000, fasta.Length);
        Assert.All(fasta.ToCharArray(), c => Assert.Contains(c, new[] { 'A', 'C', 'G', 'T' }));
    }

    [Fact]
    public async Task TestDataGenerator_SimulatesReads_CorrectCount()
    {
        var gen = new TestDataGenerator(seed: 42);
        var reference = gen.GenerateReference(length: 500);

        var reads = new List<Sequence>();
        await foreach (var r in gen.SimulateReads(reference, depth: 10, readLength: 50))
        {
            reads.Add(r);
        }

        // depth 10 × length 500 / read 50 = 100 reads expected
        Assert.InRange(reads.Count, 80, 120);
    }

    [Fact]
    public async Task TestDataGenerator_SimulatedReads_AreValidFastq()
    {
        var gen = new TestDataGenerator(seed: 42);
        var reference = gen.GenerateReference(length: 200);

        await foreach (var r in gen.SimulateReads(reference, depth: 5, readLength: 30))
        {
            Assert.Equal(30, r.Length);
            Assert.All(r.GetData().ToArray(), c => Assert.Contains(c, new[] { 'A', 'C', 'G', 'T', 'N' }));
        }
    }

    [Fact]
    public void TestDataGenerator_InjectsVariants_AtKnownPositions()
    {
        var gen = new TestDataGenerator(seed: 42);
        var reference = gen.GenerateReference(length: 500);
        var variants = new[]
        {
            new SyntheticVariant { Position = 100, ReferenceAllele = reference[100], AlternateAllele = 'T' },
            new SyntheticVariant { Position = 200, ReferenceAllele = reference[200], AlternateAllele = 'G' }
        };

        var mutated = gen.InjectVariants(reference, variants);

        Assert.Equal('T', mutated[100]);
        Assert.Equal('G', mutated[200]);
        // Surrounding bases unchanged
        Assert.Equal(reference[99], mutated[99]);
        Assert.Equal(reference[101], mutated[101]);
    }

    [Fact]
    public async Task TestDataGenerator_GenerateVariantSet_ReturnsInjectedVariants()
    {
        var gen = new TestDataGenerator(seed: 42);
        var (_, variants, reads) = await gen.GenerateVariantSet(
            referenceLength: 500, variantCount: 5, readDepth: 10, readLength: 50);

        Assert.Equal(5, variants.Count);
        Assert.NotEmpty(reads);
    }
}