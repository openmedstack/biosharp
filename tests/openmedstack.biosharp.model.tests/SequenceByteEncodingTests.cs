using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OpenMedStack.BioSharp.Model.Tests;

using Xunit;

public class SequenceByteEncodingTests
{
    private static readonly char[] _bases = "ACGT".ToCharArray();

    private static IEnumerable<byte> Encode(char[] bases)
    {
        var offset = 0;
        while (offset < bases.Length)
        {
            var bits = new BitArray(8);
            for (var i = 0; i < 4; i++)
            {
                if (offset + i >= bases.Length) break;

                var data = bases[offset + i];
                switch (data)
                {
                    case 'A':
                        break;
                    case 'C':
                        bits.Set(i * 2, true);
                        break;
                    case 'G':
                        bits.Set(i * 2 + 1, true);
                        break;
                    case 'T':
                        bits.Set(i * 2, true);
                        bits.Set(i * 2 + 1, true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(data), $"Invalid base: {data}");
                }
            }

            var buffer = new byte[1];
            bits.CopyTo(buffer, 0);
            yield return buffer[0];
            bits.SetAll(false);
            offset += 4;
        }
    }

    public static IEnumerable<char> Decode(int count, byte[] encoded)
    {
        var bits = new BitArray(encoded);
        for (var i = 0; i < count * 2; i += 2)
        {
            var index = i + 1;
            var data = bits[i] switch
            {
                false when !bits[index] => 'A',
                true when !bits[index] => 'C',
                false when bits[index] => 'G',
                true when bits[index] => 'T',
                _ => throw new InvalidOperationException($"Invalid base bits: {bits[6]}{bits[7]}")
            };
            yield return data;
        }
    }

    [Theory]
    [InlineData("ACGT")]
    [InlineData("AAAAA")]
    [InlineData("AACAA")]
    [InlineData("GACATTA")]
    public void TestEncoding(string data)
    {
        var bases = data.ToCharArray();
        var byteLength = (uint)Math.Ceiling(bases.Length / 4.0);
        var encoded = Encode(bases);
        var bytes = encoded.ToArray();
        var chars = Decode(bases.Length, bytes);
        Assert.Equal(byteLength, (uint)bytes.Length);
        Assert.Equal(data, new string(chars.ToArray()));
    }

    /// <summary>
    /// Maps a nucleotide char to a 2-bit value.
    /// A -> 0, C -> 1, G -> 2, T -> 3. Anything else throws.
    /// </summary>
    public static byte EncodeBase(char baseChar)
    {
        var idx = _bases.IndexOf(baseChar);
        if (idx < 0)
            throw new ArgumentOutOfRangeException(
                nameof(baseChar),
                $"{baseChar} is not a valid DNA base (must be A, C, G, or T).");

        return (byte)idx;
    }

    /// <summary>
    /// Maps a 2-bit value back to its nucleotide char.
    /// 0 -> A, 1 -> C, 2 -> G, 3 -> T.
    /// </summary>
    public static char DecodeBase(byte nibble)
    {
        if (nibble > 3)
            throw new ArgumentOutOfRangeException(
                nameof(nibble),
                "Nucleotide nibble must be in [0, 3].");

        return _bases[nibble];
    }

    /// <summary>
    /// Encodes a quality score char (Illumina encoding, range 33-126)
    /// into a 6-bit value (0-93) by subtracting the offset of 33.
    /// </summary>
    public static byte EncodeQuality(char qualityChar)
    {
        if (qualityChar < (char)33 || qualityChar > (char)126)
            throw new ArgumentOutOfRangeException(
                nameof(qualityChar),
                $"Quality char must be between ASCII 33 ('!') and 126 ('~'). Got '{qualityChar}'.");

        return (byte)(qualityChar - 33);
    }

    /// <summary>
    /// Decodes a 6-bit quality value back to its char.
    /// </summary>
    public static char DecodeQuality(byte val)
    {
        if (val > 93)
            throw new ArgumentOutOfRangeException(
                nameof(val),
                "Quality value must be in [0, 93].");

        return (char)(val + 33);
    }

    /// <summary>
    /// Packs a nucleotide base and a quality score into a single byte:
    ///   Bits 7-2 (upper 6 bits): quality - 33 (range 0..93)
    ///   Bits 1-0 (lower 2 bits): 0=A, 1=C, 2=G, 3=T
    /// </summary>
    public static byte EncodeSequenceByte(char baseChar, char qualityChar)
    {
        var quality6 = EncodeQuality(qualityChar);
        var base2 = EncodeBase(baseChar);
        return (byte)((quality6 << 2) | base2);
    }

    /// <summary>
    /// Unpacks a combined byte back into its base and quality chars.
    /// </summary>
    public static (char Base, char Quality) DecodeSequenceByte(byte b)
    {
        var quality6 = (byte)(b >> 2); // shift right 2 to isolate upper 6 bits
        var quality = DecodeQuality(quality6);

        var base2 = (byte)(b & 0x03); // mask lower 2 bits
        var baseChar = DecodeBase(base2);

        return (baseChar, quality);
    }

    // ─── Base encoding / decoding ──────────────────────────────────────

    [Fact]
    public void EncodeBase_EachBaseMapsCorrectly()
    {
        Assert.Equal((byte)0, EncodeBase('A'));
        Assert.Equal((byte)1, EncodeBase('C'));
        Assert.Equal((byte)2, EncodeBase('G'));
        Assert.Equal((byte)3, EncodeBase('T'));
    }

    [Fact]
    public void EncodeBase_InvalidBase_Throws()
    {
        foreach (var c in new[] { 'N', 'X', ' ' }) Assert.Throws<ArgumentOutOfRangeException>(() => EncodeBase(c));
    }

    [Fact]
    public void EncodeBase_RoundTrip()
    {
        foreach (var baseChar in _bases)
        {
            var encoded = EncodeBase(baseChar);
            var decoded = DecodeBase(encoded);
            Assert.Equal(baseChar, decoded);
        }
    }

    // ─── Quality encoding / decoding ───────────────────────────────────

    [Theory]
    [InlineData((char)33, (byte)0)] // '!' -> 0
    [InlineData((char)73, (byte)40)] // 'I' -> 40
    [InlineData((char)126, (byte)93)] // '~' -> 93
    [InlineData((char)64, (byte)31)] // '@' -> 31
    [InlineData((char)43, (byte)10)] // '+' -> 10
    [InlineData((char)54, (byte)21)] // '6' -> 21
    [InlineData((char)72, (byte)39)] // 'H' -> 39
    public void EncodeQuality_RoundTrips(char input, byte expectedOutput)
    {
        var encoded = EncodeQuality(input);
        Assert.Equal(expectedOutput, encoded);

        var decoded = DecodeQuality(encoded);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void EncodeQuality_BoundaryValues()
    {
        // Minimum valid quality
        var low = EncodeQuality((char)33);
        Assert.Equal((byte)0, low);

        // Maximum valid quality
        var high = EncodeQuality((char)126);
        Assert.Equal((byte)93, high);
    }

    [Fact]
    public void EncodeQuality_TooLow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeQuality((char)32)); // space
    }

    [Fact]
    public void EncodeQuality_TooHigh_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeQuality((char)127)); // DEL
    }

    [Fact]
    public void EncodeQuality_EnumerateAllValidChars_FitsIn6Bits()
    {
        for (var c = (char)33; c <= (char)126; c++)
        {
            var encoded = EncodeQuality(c);
            Assert.True((encoded & (byte)0xC0) == 0,
                $"{c} (ASCII {c}) encoded to {encoded} but bit 7 is set.");
        }
    }

    [Fact]
    public void DecodeQuality_TooHigh_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DecodeQuality(94)); // >93
    }

    // ─── Combined encoding with a real Sequence ────────────────────────

    [Fact]
    public void EncodeSequenceByte_AllFourBasesWithSampleQuality()
    {
        // A! = 0x00, C6 = 0x14, GI = 0x28, T~ = 0x5C
        var input = new[] { ('A', (char)33), ('C', '6'), ('G', 'I'), ('T', '~') };

        foreach (var (baseChr, qualChr) in input)
        {
            var packed = EncodeSequenceByte(baseChr, qualChr);

            // Verify the base bits are correct
            var baseBits = (byte)(packed & 0x03);
            Assert.Equal(EncodeBase(baseChr), baseBits);

            // Verify the quality bits are correct
            var qualBits = (byte)(packed >> 2);
            Assert.Equal(EncodeQuality(qualChr), qualBits);
        }
    }

    [Fact]
    public void EncodeSequenceByte_RoundTripFullSequence()
    {
        // ACGT with quality scores '!II~'
        var data = new ReadOnlyMemory<char>("ACGT".ToCharArray());
        var quals = new ReadOnlyMemory<char>("!II~".ToCharArray());

        var sequence = new Sequence("test", data, quals);

        var packed = new byte[sequence.Length];
        for (var i = 0; i < sequence.Length; i++)
            packed[i] = EncodeSequenceByte(sequence[i], sequence.GetQuality().Span[i]);

        for (var i = 0; i < sequence.Length; i++)
        {
            var (decodedBase, decodedQual) = DecodeSequenceByte(packed[i]);
            Assert.Equal(sequence[i], decodedBase);
            Assert.Equal(sequence.GetQuality().Span[i], decodedQual);
        }
    }

    [Fact]
    public void EncodeSequenceByte_AllQualityScoreRange_RoundTrips()
    {
        // Stress-test: every quality score (33..126) with all 4 bases.
        var bases = "ACGT".ToCharArray();

        for (var qual = (char)33; qual <= (char)126; qual++)
            foreach (var baseChr in bases)
            {
                var packed = EncodeSequenceByte(baseChr, qual);
                var (decodedBase, decodedQual) = DecodeSequenceByte(packed);
                Assert.Equal(baseChr, decodedBase);
                Assert.Equal(qual, decodedQual);
            }
    }

    [Fact]
    public void EncodeSequenceByte_SeparatesBaseAndQualityBits()
    {
        // Ensure that changing the quality doesn't affect the base bits,
        // and that changing the base doesn't affect the quality bits.
        var baseChr = 'G';
        var qualities = new char[] { (char)33, (char)73, (char)126 };
        var expectedBaseBits = EncodeBase(baseChr);

        foreach (var qual in qualities)
        {
            var packed = EncodeSequenceByte(baseChr, qual);
            var actualBaseBits = (byte)(packed & 0x03);
            Assert.Equal(expectedBaseBits, actualBaseBits);
        }

        var bases = new char[] { 'A', 'C', 'G', 'T' };
        var expectedQualBits = EncodeQuality((char)50); // '2' -> 17

        foreach (var nucleotide in bases)
        {
            var packed = EncodeSequenceByte(nucleotide, (char)50);
            var actualQual = (byte)(packed >> 2);
            Assert.Equal(expectedQualBits, actualQual);
        }
    }

    [Fact]
    public void EncodeSequenceByte_VariousSequences_AllRoundTrip()
    {
        var sequences = new[]
        {
            ("short", "A", "!"),
            ("medium", "ACGTACGT", "!II~!II~"),
            ("all_quality", "AAAAAAAAAA", "!!!!!!!!!!"),
            ("high_qual", "TTTTTTTTTT", "~~~~~~~~~~~~~~~~"),
            ("mixed", "ACGTACGT", "+@59H>2<")
        };

        foreach (var (id, data, quals) in sequences)
        {
            var dataMem = new ReadOnlyMemory<char>(data.ToCharArray());
            var qualsMem = new ReadOnlyMemory<char>(quals.ToCharArray());

            var sequence = new Sequence(id, dataMem, qualsMem);

            var packed = new byte[sequence.Length];
            for (var i = 0; i < sequence.Length; i++)
                packed[i] = EncodeSequenceByte(sequence[i], sequence.GetQuality().Span[i]);

            for (var i = 0; i < sequence.Length; i++)
            {
                var (decodedBase, decodedQual) = DecodeSequenceByte(packed[i]);
                Assert.Equal(sequence[i], decodedBase);
                Assert.Equal(sequence.GetQuality().Span[i], decodedQual);
            }
        }
    }
}
