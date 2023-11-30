using System.Runtime.CompilerServices;

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
        private readonly BgzfStream _stream;
        private readonly ILogger<BamReader> _logger;
        private static readonly byte[] MagicHeader = { 66, 65, 77, 0x01 };

        public BamReader(string filePath, ILogger<BamReader> logger)
        {
            _logger = logger;
            var file = File.Open(
                filePath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            _stream = new BgzfStream(file, CompressionMode.Decompress, false);
        }

        public BamReader(Stream stream, ILogger<BamReader> logger)
        {
            _stream = stream as BgzfStream ?? new BgzfStream(stream, CompressionMode.Decompress, false);
            _logger = logger;
        }

        public async Task<SamDefinition> Read(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Start reading");

            var arrayPool = ArrayPool<byte>.Shared;
            var buffer = arrayPool.Rent(1024);
            var mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            if (mem.Length != 4 || !mem.Span.SequenceEqual(MagicHeader))
            {
                throw new InvalidDataException("Invalid Header");
            }

            mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

            var textLength = (int)BitConverter.ToUInt32(mem.Span);
            if (textLength > buffer.Length)
            {
                arrayPool.Return(buffer);
                buffer = arrayPool.Rent(textLength);
            }

            mem = await _stream.FillBuffer(buffer.AsMemory(0, textLength), cancellationToken).ConfigureAwait(false);

            var text = textLength == 0 ? "" : Encoding.UTF8.GetString(mem[..^1].Span);

            mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);

            var numberOfReferences = BitConverter.ToUInt32(mem.Span);

            var refSeqs = new HashSet<ReferenceSequence>();
            for (var i = 0; i < numberOfReferences; i++)
            {
                var referenceSequence = await ReadReferenceSequence(_stream, buffer, cancellationToken);
                refSeqs.Add(referenceSequence);
            }

            var (fmd, pg, rg, rs) = ReadFileMetadata(text);
            refSeqs.UnionWith(rs);

            if (refSeqs.Count != numberOfReferences)
            {
                throw new InvalidDataException("Inconsistent reference count");
            }

            var alignments = await ReadAlignments(cancellationToken).ToListAsync(cancellationToken);

            var content = new SamDefinition(fmd, refSeqs, rg, pg, alignments);

            arrayPool.Return(buffer);
            return content;

            async IAsyncEnumerable<AlignmentSection> ReadAlignments([EnumeratorCancellation] CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    mem = await _stream.FillBuffer(buffer.AsMemory(0, 4), true, cancellationToken)
                        .ConfigureAwait(false);
                    if (mem.Length < 4)
                    {
                        break;
                    }

                    var blockSize = (int)BitConverter.ToUInt32(mem.Span);
                    var blockBuffer = arrayPool.Rent(blockSize);
                    var block = await _stream.FillBuffer(blockBuffer.AsMemory(0, blockSize), cancellationToken)
                        .ConfigureAwait(false);
                    yield return ProcessBlock(block);
                }
            }
        }

        private static (FileMetadata fmd, Program pg, ReadGroup rg, HashSet<ReferenceSequence> refSeqs)
            ReadFileMetadata(string text)
        {
            FileMetadata fmd = null!;
            Program pg = null!;
            ReadGroup rg = null!;
            HashSet<ReferenceSequence> refSeqs = new();
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

            return (fmd, pg, rg, refSeqs);
        }

        private static async Task<ReferenceSequence> ReadReferenceSequence(
            Stream file,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            var nameLength = (int)BitConverter.ToUInt32(
                (await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false)).Span);

            var name = Encoding.UTF8.GetString(
                (await file.FillBuffer(buffer.AsMemory(0, nameLength), cancellationToken).ConfigureAwait(false))
                .Span[..(nameLength - 1)]);

            var length = await file.FillBuffer(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            var referenceSequence = new ReferenceSequence(name, BitConverter.ToUInt32(length.Span));
            return referenceSequence;
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
            var cigars = new (uint, CigarOp)[cigarLength / 4];

            for (var i = 0; i < cigars.Length; i++)
            {
                var encoded = BitConverter.ToUInt32(ops.Slice(i * 4, 4));
                cigars[i] = encoded.Decode();
            }

            var tags = ReadTags(block[tagStart..]).ToArray();

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
                    return (length + 1, Encoding.UTF8.GetString(span[..length]));
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
