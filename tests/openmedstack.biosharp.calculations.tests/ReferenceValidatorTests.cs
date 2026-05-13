using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Io.FastA;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class ReferenceValidatorTests
{
    [Fact]
    public async Task ReferenceValidator_ComputesChecksums_ForKnownContent()
    {
        // Write a small fake FASTA
        var content = ">chr1\nACGTACGT\n";
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var result = await ReferenceValidator.ComputeChecksums(ms);

        Assert.NotNull(result.Md5);
        Assert.NotNull(result.Sha256);
        Assert.False(string.IsNullOrEmpty(result.Md5));
        Assert.False(string.IsNullOrEmpty(result.Sha256));
    }

    [Fact]
    public async Task ReferenceValidator_SameFastaProducesSameChecksum()
    {
        var content = ">chr1\nACGTACGT\n";
        using var ms1 = new MemoryStream(Encoding.ASCII.GetBytes(content));
        using var ms2 = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var r1 = await ReferenceValidator.ComputeChecksums(ms1);
        var r2 = await ReferenceValidator.ComputeChecksums(ms2);

        Assert.Equal(r1.Md5, r2.Md5);
        Assert.Equal(r1.Sha256, r2.Sha256);
    }

    [Fact]
    public async Task ReferenceValidator_Validate_ThrowsOnChecksuMismatch()
    {
        var content = ">chr1\nACGTACGT\n";
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        await Assert.ThrowsAsync<ReferenceValidationException>(
            () => ReferenceValidator.Validate(ms, expectedMd5: "wrong_checksum"));
    }

    [Fact]
    public async Task ReferenceValidator_Validate_SucceedsWithCorrectChecksum()
    {
        var content = ">chr1\nACGTACGT\n";
        var bytes = Encoding.ASCII.GetBytes(content);
        var expectedMd5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

        using var ms = new MemoryStream(bytes);
        // Should not throw
        await ReferenceValidator.Validate(ms, expectedMd5: expectedMd5);
    }
}