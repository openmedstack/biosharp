namespace OpenMedStack.BioSharp.Io.Bam
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Sam;

    public class BamReader
    {
        // private static readonly byte[] EofSequence = { 0x1f, 0x8b, 0x08, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0x06, 0x00, 0x42, 0x43, 0x02, 0x00, 0x1b, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private readonly ILogger _logger;
        private static readonly byte[] MagicHeader = { 66, 65, 77, 0x01 };

        public BamReader(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<SamDefinition> Read(string filePath, CancellationToken cancellationToken = default)
        {
            var file = File.Open(
                filePath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            await using var _ = file.ConfigureAwait(false);
            var zip = new GZipStream(file, CompressionMode.Decompress, true);
            await using var __ = zip.ConfigureAwait(false);
            return await Read(zip, cancellationToken).ConfigureAwait(false);
        }

        public async Task<SamDefinition> Read(Stream file, CancellationToken cancellationToken = default)
        {
            if (file is GZipStream zipStream)
            {
                return await Read(zipStream, cancellationToken).ConfigureAwait(false);
            }

            var zip = new GZipStream(file, CompressionMode.Decompress, true);
            await using var _ = zip.ConfigureAwait(false);
            return await Read(zip, cancellationToken).ConfigureAwait(false);
        }

        private async Task<SamDefinition> Read(GZipStream file, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Start reading");

            var arrayPool = ArrayPool<byte>.Shared;
            var buffer = arrayPool.Rent(1024);
            var mem = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (mem.Length != 4 || !mem.Span.SequenceEqual(MagicHeader))
            {
                throw new InvalidDataException("Invalid Header");
            }

            mem = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

            var textLength = (int)BitConverter.ToUInt32(mem.Span);
            if (textLength > buffer.Length)
            {
                arrayPool.Return(buffer);
                buffer = arrayPool.Rent(textLength);
            }

            mem = await file.FillBuffer(buffer.AsMemory(0, textLength), cancellationToken).ConfigureAwait(false);

            var text = textLength == 0 ? "" : Encoding.UTF8.GetString(mem[..^1].Span);

            mem = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

            var numberOfReferences = BitConverter.ToUInt32(mem.Span);

            var refSeqs = new HashSet<ReferenceSequence>();
            for (var i = 0; i < numberOfReferences; i++)
            {
                var nameLength = (int)BitConverter.ToUInt32(
                    (await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false)).Span);

                var name = Encoding.UTF8.GetString(
                    (await file.FillBuffer(buffer.AsMemory(0, nameLength), cancellationToken).ConfigureAwait(false))
                    .Span[..(nameLength - 1)]);

                var length = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                var referenceSequence = new ReferenceSequence(name, BitConverter.ToUInt32(length.Span));
                refSeqs.Add(referenceSequence);
            }

            FileMetadata fmd = null!;
            Program pg = null!;
            ReadGroup rg = null!;
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries))
                {
                    var span = line.Substring(1, 2);
                    switch (span)
                    {
                        case "HD":
                            fmd = FileMetadata.Parse(line);
                            break;
                        case "SQ":
                            refSeqs.Add(ReferenceSequence.Parse(line));
                            break;
                        case "PG":
                            pg = Program.Parse(line);
                            break;
                        case "RG":
                            rg = ReadGroup.Parse(line);
                            break;
                    }
                }
            }
            else
            {
                fmd = new FileMetadata("1.0", FileMetadata.SortingOrder.unknown, FileMetadata.AlignmentGrouping.none);
                pg = new Program("");
                rg = new ReadGroup("");
            }

            if (refSeqs.Count != numberOfReferences)
            {
                throw new InvalidDataException("Inconsistent reference count");
            }

            var alignments = new List<AlignmentSection>();
            while (file.BaseStream.Position < file.BaseStream.Length)
            {
                mem = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

                var blockSize = (int)BitConverter.ToUInt32(mem.Span);
                var blockBuffer = arrayPool.Rent(blockSize);
                var block = await file.FillBuffer(blockBuffer.AsMemory(0, blockSize), cancellationToken)
                    .ConfigureAwait(false);
                alignments.Add(ProcessBlock(block));
            }

            var content = new SamDefinition(fmd, refSeqs, rg, pg, alignments);

            arrayPool.Return(buffer);
            return content;
        }

        private static AlignmentSection ProcessBlock(Memory<byte> block)
        {
            var position = BitConverter.ToInt32(block.Slice(4, 4).Span) + 1;
            var nameLength = block.Span[8];
            var mapq = block.Span[9];
            var bamBinary = BitConverter.ToUInt16(block.Slice(10, 2).Span);
            var cigarLength = BitConverter.ToUInt16(block.Slice(12, 2).Span) * 4;
            var alignmentFlag = (AlignmentSection.AlignmentFlag)BitConverter.ToUInt16(block.Slice(14, 2).Span);
            var baseSeqLength = (int)BitConverter.ToUInt32(block.Slice(16, 4).Span);
            var rnext = BitConverter.ToInt32(block.Slice(20, 4).Span);
            var pnext = BitConverter.ToInt32(block.Slice(24, 4).Span) - 1;
            var templateLength = BitConverter.ToInt32(block.Slice(28, 4).Span);
            var qName = Encoding.UTF8.GetString(block.Slice(32, nameLength - 1).Span);
            var sequenceLength = ((baseSeqLength + 1) / 2);
            var cigarStart = 32 + nameLength;
            var sequenceStart = cigarStart + cigarLength;
            var qStart = sequenceStart + sequenceLength;
            var tagStart = qStart + baseSeqLength;
            var ops = block.Slice(cigarStart, cigarLength).Span;
            var cigars = new (uint, char)[cigarLength / 4];

            for (var i = 0; i < cigars.Length; i++)
            {
                var encoded = BitConverter.ToUInt32(ops.Slice(i * 4, 4));
                cigars[i] = encoded.Decode();
            }

            var tags = Enumerable.Empty<AlignmentTag>(); // ReadTags(block[tagStart..]).ToArray();

            var sequence = block.Slice(sequenceStart, sequenceLength).Span.ReadSequence();
            var qualScores = block.Slice(qStart, baseSeqLength);
            var qualityChars = Array.ConvertAll(qualScores.ToArray(), b => b == 255 ? ' ' : (char)b);
            var quality = new string(qualityChars);
            return new AlignmentSection(
                qName,
                alignmentFlag,
                qName,
                position,
                mapq,
                cigars,
                rnext,
                pnext,
                templateLength,
                sequence,
                quality,
                tags,
                bamBinary);
        }

        private static IEnumerable<AlignmentTag> ReadTags(Memory<byte> memory)
        {
            var position = 0;
            while (position < memory.Length)
            {
                var key = Encoding.UTF8.GetString(memory.Span[..2]);
                var type = (char)memory.Span[2];
                var (length, value) = ReadTagValue(memory.Span[3..], type);

                position += (3 + length);
                yield return new AlignmentTag(key, type, value);
            }
        }

        private static (int length, object value) ReadTagValue(Span<byte> span, char type)
        {
            switch (type)
            {
                case 'A':
                    return (1, (char)span[0]);
                case 'Z':
                {
                    var length = span.IndexOf((byte)'\0');
                    return (length + 1, Encoding.UTF8.GetString(span[..(length)]));
                }
                case 'i':
                {
                    return (4, BitConverter.ToInt32(span[..4]));
                }
                case 'I':
                {
                    return (4, BitConverter.ToUInt32(span[..4]));
                }
                case 's':
                {
                    return (2, BitConverter.ToInt16(span[..2]));
                }
                case 'S':
                {
                    return (2, BitConverter.ToUInt16(span[..2]));
                }
                case 'c':
                {
                    return (1, (sbyte)(span[0]));
                }
                case 'C':
                {
                    return (1, span[0]);
                }
                case 'f':
                {
                    return (4, BitConverter.ToSingle(span[..4]));
                }
                case 'H':
                {
                    return (2, Encoding.UTF8.GetString(span[..2]));
                }
                case 'B':
                {
                    var subtype = (char)span[0];
                    var count = BitConverter.ToInt32(span.Slice(1, 4));
                    var total = 5;
                    var array = Array.CreateInstance(GetTagType(type), count);
                    for (var i = 0; i < count; i++)
                    {
                        var (length, value) = ReadTagValue(span[5..], subtype);
                        total += length;
                        array.SetValue(value, i);
                    }

                    return (total, array);
                }
                default: throw new InvalidDataException($"Invalid tag type: {type}");
            }
        }

        private static Type GetTagType(char type)
        {
            return type switch
            {
                'A' => typeof(char),
                'i' => typeof(int),
                'I' => typeof(uint),
                'f' => typeof(float),
                'Z' => typeof(string),
                'H' => typeof(char[]),
                'c' => typeof(sbyte),
                'C' => typeof(byte),
                's' => typeof(short),
                'S' => typeof(ushort),
                _ => throw new InvalidDataException("Invalid tag value")
            };
        }
    }
}
