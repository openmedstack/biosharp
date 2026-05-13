using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class MultithreadedReadingTests
{
    private static string BuildFastqContent(int readCount, int readLength = 50)
    {
        var sb = new StringBuilder();
        var bases = "ACGT";
        var rng = new Random(42);
        for (var i = 0; i < readCount; i++)
        {
            sb.AppendLine($"@read_{i}");
            for (var j = 0; j < readLength; j++)
            {
                sb.Append(bases[rng.Next(4)]);
            }

            sb.AppendLine();
            sb.AppendLine("+");
            sb.Append(new string('I', readLength));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Fact]
    public async Task FastQReader_ReadAheadBuffer_ReturnsAllRecords()
    {
        const int readCount = 1000;
        var content = BuildFastqContent(readCount);
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var reader = new Io.FastQ.FastQReader(NullLogger.Instance);

        var reads = new List<Sequence>();
        await foreach (var seq in reader.ReadWithReadAhead(ms))
        {
            reads.Add(seq);
        }

        Assert.Equal(readCount, reads.Count);
    }

    [Fact]
    public async Task FastQReader_ReadAheadBuffer_NoRecordLostOrDuplicated()
    {
        const int readCount = 500;
        var content = BuildFastqContent(readCount);
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var reader = new Io.FastQ.FastQReader(
            NullLogger.Instance);

        var readIds = new HashSet<string>();
        await foreach (var seq in reader.ReadWithReadAhead(ms))
        {
            Assert.True(readIds.Add(seq.Id), $"Duplicate read detected: {seq.Id}");
        }
        Assert.Equal(readCount, readIds.Count);
    }

    [Fact]
    public async Task FastQReader_ReadAheadBuffer_PreservesRecordOrder()
    {
        const int readCount = 100;
        var content = BuildFastqContent(readCount);
        using var ms1 = new MemoryStream(Encoding.ASCII.GetBytes(content));
        using var ms2 = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var reader = new Io.FastQ.FastQReader(
            NullLogger.Instance);

        var sequential = new List<string>();
        await foreach (var seq in reader.Read(ms1))
        {
            sequential.Add(seq.Id);
        }

        var parallel = new List<string>();
        await foreach (var seq in reader.ReadWithReadAhead(ms2))
        {
            parallel.Add(seq.Id);
        }

        Assert.Equal(sequential, parallel);
    }
}