using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenMedStack.BioSharp.Io.Bgzf;
using OpenMedStack.BioSharp.Io.Sam;

namespace OpenMedStack.BioSharp.Io.Bam;

public class BamWriter
{
    private static readonly byte[] MagicHeader = [66, 65, 77, 0x01];
    private readonly BgzfStream _stream;
    private readonly ILogger<BamWriter> _logger;

    public BamWriter(BgzfStream stream, ILogger<BamWriter> logger)
    {
        _stream = stream;
        _logger = logger;
    }

    public async Task Write(SamDefinition definition, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Writing BAM content");

        await _stream.WriteAsync(MagicHeader.AsMemory(0, 4), cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine(definition.Hd.ToString());
        builder.AppendLine(definition.Pg.ToString());
        builder.AppendLine(definition.Rg.ToString());
        foreach (var referenceSequence in definition.Sq)
        {
            builder.AppendLine(referenceSequence.ToString());
        }

        var header = Encoding.UTF8.GetBytes(builder.ToString().Trim());
        await using var binaryWriter = new BinaryWriter(_stream, Encoding.UTF8, true);
        binaryWriter.Write(header.Length + 1); // +1 for NUL terminator expected by reader
        await _stream.WriteAsync(header, cancellationToken);
        binaryWriter.Write((byte)0); // NUL terminator
        binaryWriter.Write(definition.Sq.Length);
        foreach (var sequence in definition.Sq)
        {
            var name = Encoding.UTF8.GetBytes(sequence.Name);
            binaryWriter.Write(name.Length + 1); // l_name includes NUL per BAM spec
            await _stream.WriteAsync(name, cancellationToken);
            binaryWriter.Write((byte)0); // NUL terminator
            binaryWriter.Write(sequence.Length);
        }

        using var block = new MemoryStream(4096);
        foreach (var alignmentSection in definition.AlignmentSections)
        {
            block.SetLength(0);
            FillBlock(block, alignmentSection);
            var blockLen = (int)block.Length;
            binaryWriter.Write((uint)blockLen);
            await _stream.WriteAsync(block.GetBuffer().AsMemory(0, blockLen), cancellationToken).ConfigureAwait(false);
        }
    }

    private void FillBlock(MemoryStream block, AlignmentSection alignmentSection)
    {
        block.Write(BitConverter.GetBytes(-1));
        block.Write(BitConverter.GetBytes(alignmentSection.Position - 1));
        var readName = Encoding.UTF8.GetBytes(alignmentSection.ReadName);
        block.WriteByte((byte)(readName.Length + 1));
        block.WriteByte(alignmentSection.MappingQuality);
        block.Write(BitConverter.GetBytes((ushort)alignmentSection.Index));
        block.Write(BitConverter.GetBytes((ushort)alignmentSection.Cigar.Length));
        block.Write(BitConverter.GetBytes((ushort)alignmentSection.Flag));
        block.Write(BitConverter.GetBytes((uint)alignmentSection.Sequence.Length));
        block.Write(BitConverter.GetBytes(alignmentSection.ReferenceIdOfNextSegment));
        block.Write(BitConverter.GetBytes(alignmentSection.NextPosition));
        block.Write(BitConverter.GetBytes(alignmentSection.TemplateLength));
        block.Write(readName);
        block.WriteByte((byte)'\0');
        foreach (var cigar in alignmentSection.Cigar)
        {
            block.Write(BitConverter.GetBytes(cigar.Encode()));
        }

        block.Write(alignmentSection.Sequence.WriteSequence());
        var quality = alignmentSection.Quality.AsSpan();
        var qualBytes = quality.Length <= 512 ? stackalloc byte[quality.Length] : new byte[quality.Length];
        for (var i = 0; i < quality.Length; i++)
        {
            qualBytes[i] = quality[i] == ' ' ? (byte)255 : (byte)quality[i];
        }

        block.Write(qualBytes);

        foreach (var tag in alignmentSection.Tags)
        {
            block.WriteByte((byte)tag.Key[0]);
            block.WriteByte((byte)tag.Key[1]);
            block.WriteByte((byte)tag.Type);
            block.Write(GetTagValueBytes(tag.Type, tag.Value));
        }
    }

    private byte[] GetTagValueBytes(char type, object tagValue)
    {
        switch (type)
        {
            case 'A':
                return [(byte)tagValue];
            case 'Z':
            {
                var s = (string)tagValue;
                var bytes = new byte[s.Length + 1];
                for (var i = 0; i < s.Length; i++)
                {
                    bytes[i] = (byte)s[i];
                }

                bytes[^1] = (byte)'\0';
                return bytes;
            }
            case 'i':
            {
                return BitConverter.GetBytes((int)tagValue);
            }
            case 'I':
            {
                return BitConverter.GetBytes((uint)tagValue);
            }
            case 's':
            {
                return BitConverter.GetBytes((short)tagValue);
            }
            case 'S':
            {
                return BitConverter.GetBytes((ushort)tagValue);
            }
            case 'c':
            {
                return [(byte)(sbyte)tagValue];
            }
            case 'C':
            {
                return [(byte)tagValue];
            }
            case 'f':
            {
                return BitConverter.GetBytes((float)tagValue);
            }
            case 'H':
            {
                return Encoding.UTF8.GetBytes((char[])tagValue);
            }
            case 'B':
            {
                if (!tagValue.GetType().IsArray)
                {
                    throw new InvalidDataException("Not an array");
                }

                var subtype = (byte)GetTagType(tagValue.GetType().GetElementType()!);
                var bytes = GetArrayBytes((char)subtype, tagValue);
                var count = BitConverter.GetBytes(bytes.Length);
                var result = new byte[5 + bytes.Length];
                result[0] = subtype;
                count.CopyTo(result, 1);
                var index = 5;
                foreach (var t in bytes)
                {
                    t.CopyTo(result, index);
                    index += t.Length;
                }

                return result;
            }
            default: throw new InvalidDataException($"Invalid tag type: {type}");
        }
    }

    private byte[][] GetArrayBytes(char type, object array)
    {
        return type switch
        {
            'A' => ((char[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'i' => ((int[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'I' => ((uint[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'f' => ((float[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'Z' => ((string[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'H' => ((char[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'c' => ((sbyte[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'C' => ((byte[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            's' => ((short[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            'S' => ((ushort[])array).Select(x => GetTagValueBytes(type, x)).ToArray(),
            _ => throw new InvalidDataException("Invalid tag value")
        };
    }

    private static char GetTagType(Type type)
    {
        if (type == typeof(char))
        {
            return 'A';
        }

        if (type == typeof(int))
        {
            return 'i';
        }

        if (type == typeof(uint))
        {
            return 'I';
        }

        if (type == typeof(float))
        {
            return 'f';
        }

        if (type == typeof(string))
        {
            return 'Z';
        }

        if (type == typeof(char[]))
        {
            return 'H';
        }

        if (type == typeof(sbyte))
        {
            return 'c';
        }

        if (type == typeof(byte))
        {
            return 'C';
        }

        if (type == typeof(short))
        {
            return 's';
        }

        return type == typeof(ushort) ? 'S' : throw new InvalidDataException("Invalid tag value");
    }
}
