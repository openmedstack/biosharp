using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenMedStack.BioSharp.Io.Sam;

namespace OpenMedStack.BioSharp.Io.Bam;

public class BamWriter
{
    private static readonly byte[] MagicHeader = { 66, 65, 77, 0x01 };
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
        binaryWriter.Write(header.Length);
        await _stream.WriteAsync(header, cancellationToken);
        binaryWriter.Write(definition.Sq.Length);
        foreach (var sequence in definition.Sq)
        {
            var name = Encoding.UTF8.GetBytes(sequence.Name);
            binaryWriter.Write(name.Length);
            await _stream.WriteAsync(name, cancellationToken);
            binaryWriter.Write(sequence.Length);
        }

        var block = new List<byte>();
        foreach (var alignmentSection in definition.AlignmentSections)
        {
            FillBlock(block, alignmentSection);

            binaryWriter.Write((uint)block.Count);
            await _stream.WriteAsync(block.ToArray(), cancellationToken).ConfigureAwait(false);
            block.Clear();
        }
    }

    private void FillBlock(List<byte> block, AlignmentSection alignmentSection)
    {
        block.AddRange(BitConverter.GetBytes(-1));
        block.AddRange(BitConverter.GetBytes(alignmentSection.Position - 1));
        var readName = Encoding.UTF8.GetBytes(alignmentSection.ReadName);
        block.Add((byte)(readName.Length + 1));
        block.Add(alignmentSection.MappingQuality);
        block.AddRange(BitConverter.GetBytes((ushort)alignmentSection.Index));
        block.AddRange(BitConverter.GetBytes((ushort)alignmentSection.Cigar.Length));
        block.AddRange(BitConverter.GetBytes((ushort)alignmentSection.Flag));
        block.AddRange(BitConverter.GetBytes((uint)alignmentSection.Sequence.Length));
        block.AddRange(BitConverter.GetBytes(alignmentSection.ReferenceIdOfNextSegment));
        block.AddRange(BitConverter.GetBytes(alignmentSection.NextPosition));
        block.AddRange(BitConverter.GetBytes(alignmentSection.TemplateLength));
        block.AddRange(readName.AsSpan());
        block.Add((byte)'\0');
        block.AddRange(alignmentSection.Cigar.SelectMany(x => BitConverter.GetBytes(x.Encode())));
        block.AddRange(alignmentSection.Sequence.WriteSequence());
        block.AddRange(Array.ConvertAll(alignmentSection.Quality.ToCharArray(), x => x == ' ' ? (byte)255 : (byte)(x)));

        foreach (var tag in alignmentSection.Tags)
        {
            var key = Encoding.UTF8.GetBytes(tag.Key[..2]);
            block.AddRange(key);
            block.Add((byte)tag.Type);
            block.AddRange(GetTagValueBytes(tag.Type, tag.Value));
        }
    }

    private byte[] GetTagValueBytes(char type, object tagValue)
    {
        switch (type)
        {
            case 'A':
                return new[] { (byte)tagValue };
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
                return new[] { (byte)(sbyte)tagValue };
            }
            case 'C':
            {
                return new[] { (byte)tagValue };
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

        if (type == typeof(ushort))
        {
            return 'S';
        }

        throw new InvalidDataException("Invalid tag value");
    }
}
