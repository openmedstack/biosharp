using System.Linq;

namespace OpenMedStack.BioSharp.Io.Tests;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Xunit;

public class BgzfStreamTests
{
    [Fact]
    public void VerifyBamHeader()
    {
        var file = "mapt.NA12156.altex.bam";
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader(stream);
        for (var i = 0; i < 3; i++)
        {
            // ID1
            Assert.Equal(31, stream.ReadByte());
            // ID2
            Assert.Equal(139, stream.ReadByte());
            // Compression method
            Assert.Equal(8, stream.ReadByte());
            // Flags
            Assert.Equal(4, stream.ReadByte());
            // Modified time
            Assert.Equal(0u, reader.ReadUInt32());
            // Extra flags
            Assert.Equal(0, stream.ReadByte());
            //OS
            Assert.Equal(255, stream.ReadByte());
            // XLEN
            Assert.Equal(6, reader.ReadUInt16());
            // SI1
            Assert.Equal(66, stream.ReadByte());
            // SI2
            Assert.Equal(67, stream.ReadByte());
            // SLEN
            Assert.Equal(2, reader.ReadUInt16());
            var blockSize = reader.ReadUInt16(); //length
            var buffer = new byte[blockSize - 25];
            stream.ReadExactly(buffer);
            reader.ReadUInt32();
            var iSize = reader.ReadUInt32();
            using var ms = new MemoryStream(buffer);
            using var gzip = new DeflateStream(ms, CompressionMode.Decompress);
            var bytes = new byte[iSize];
            gzip.ReadExactly(bytes);
        }
    }

    [Fact]
    public async Task WhenWritingTwoEntriesThenCanReadOnlySecond()
    {
        const string first = "Hello, World";
        const string second = "Goodbye, World";
        using var ms = new MemoryStream();
        BlockOffsetRecord position = default;
        var data = Encoding.UTF8.GetBytes(second);
        await using (var bgzf = new BgzfStream(ms, CompressionLevel.NoCompression))
        {
            await bgzf.WriteAsync(Encoding.UTF8.GetBytes(first));
            position = bgzf.BlockOffset;
            await bgzf.WriteAsync(data);
        }

        ms.Position = 0;

        await using var gzip = new BgzfStream(ms, CompressionMode.Decompress);
        gzip.Seek((long)position, SeekOrigin.Begin);
        var readBuffer = new byte[14];
        var read = 0;
        while (read < readBuffer.Length)
        {
            read += await gzip.ReadAsync(readBuffer.AsMemory(read, readBuffer.Length - read));
        }

        Assert.Equal(data, readBuffer);
        Assert.Equal(second, Encoding.UTF8.GetString(readBuffer));
        Assert.Equal(data.Length, read);
    }

    [Fact]
    public async Task CanRoundTripDataToStream()
    {
        const string helloWorld = "Hello, World";
        var data = Encoding.UTF8.GetBytes(helloWorld);
        using var ms = new MemoryStream();
        await using (var bgzf = new BgzfStream(ms, CompressionLevel.NoCompression))
        {
            await bgzf.WriteAsync(data);
        }

        ms.Position = 0;
        await using var gzip = new BgzfStream(ms, CompressionMode.Decompress);
        var readBuffer = new byte[data.Length];
        var read = 0;
        while (read < data.Length)
        {
            read += await gzip.ReadAsync(readBuffer.AsMemory(read, readBuffer.Length - read));
        }

        Assert.Equal(data, readBuffer);
        Assert.Equal(helloWorld, Encoding.UTF8.GetString(readBuffer));
        Assert.Equal(data.Length, read);
    }

    [Fact]
    public void CanRoundTripLargeDataToStream()
    {
        var repetitions = Random.Shared.Next(128 * 1024, 1024 * 1024);
        var data = Encoding.UTF8.GetBytes(string.Join(',', Enumerable.Repeat("Hello, World", repetitions)));
        using var ms = new MemoryStream();
        using (var bgzf = new BgzfStream(ms, CompressionLevel.SmallestSize))
        {
            bgzf.Write(data);
        }

        ms.Position = 0;
        using var gzip = new BgzfStream(ms, CompressionMode.Decompress);
        var readBuffer = new byte[data.Length];
        var read = gzip.Read(readBuffer, 0, data.Length);

        Assert.Equal(data, readBuffer);
        Assert.Equal(data.Length, read);
    }

    [Fact]
    public async Task CanAsyncRoundTripLargeDataToStream()
    {
        var repetitions = Random.Shared.Next(128 * 1024, 1024 * 1024);
        var data = Encoding.UTF8.GetBytes(string.Join(';', Enumerable.Repeat("Hello, World", repetitions)));
        using var ms = new MemoryStream();
        await using (var bgzf = new BgzfStream(ms, CompressionLevel.SmallestSize))
        {
            await bgzf.WriteAsync(data);
        }

        ms.Position = 0;
        await using var gzip = new BgzfStream(ms, CompressionMode.Decompress);
        var readBuffer = new byte[data.Length];
        var read = await gzip.ReadAsync(readBuffer, 0, data.Length);

        Assert.Equal(data, readBuffer);
        Assert.Equal(data.Length, read);
    }

    [Fact]
    public async Task CanAsyncRoundTripDataToStream()
    {
        const string helloWorld = "Hello, World";
        var data = Encoding.UTF8.GetBytes(helloWorld);
        await using var ms = new MemoryStream();
        await using (var bgzf = new BgzfStream(ms, CompressionLevel.Fastest))
        {
            await bgzf.WriteAsync(data);
        }

        ms.Position = 0;
        await using var gzip = new BgzfStream(ms, CompressionMode.Decompress);
        var readBuffer = new byte[data.Length];
        var read = 0;
        while (read < data.Length)
        {
            read += await gzip.ReadAsync(readBuffer, read, data.Length - read);
        }

        Assert.Equal(data, readBuffer);
        Assert.Equal(helloWorld, Encoding.UTF8.GetString(readBuffer));
        Assert.Equal(data.Length, read);
    }
}
