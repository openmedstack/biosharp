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
    public void CanRoundTripDataToStream()
    {
        const string helloWorld = "Hello, World";
        var data = Encoding.UTF8.GetBytes(helloWorld);
        using var ms = new MemoryStream();
        using (var bgzf = new BgzfStream(ms, CompressionLevel.Fastest))
        {
            bgzf.Write(data);
        }

        ms.Position = 0;
        using var gzip = new BgzfStream(ms, CompressionMode.Decompress);
        var readBuffer = new byte[data.Length];
        var read = 0;
        while (read < data.Length)
        {
            read += gzip.Read(readBuffer, read, data.Length - read);
        }

        Assert.Equal(data, readBuffer);
        Assert.Equal(helloWorld, Encoding.UTF8.GetString(readBuffer));
        Assert.Equal(data.Length, read);
    }

    [Fact]
    public void CanRoundTripLargeDataToStream()
    {
        var data = new byte[Random.Shared.Next(128 * 1024, 1024 * 1024)];
        Random.Shared.NextBytes(data);
        using var ms = new MemoryStream();
        using (var bgzf = new BgzfStream(ms, CompressionLevel.Fastest))
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

    [Fact]
    public async Task CanAsyncRoundTripLargeDataToStream()
    {
        var data = new byte[Random.Shared.Next(128 * 1024, 1024 * 1024)];
        Random.Shared.NextBytes(data);
        await using var ms = new MemoryStream();
        await using (var bgzf = new BgzfStream(ms, CompressionLevel.Fastest))
        {
            await bgzf.WriteAsync(data);
        }

        ms.Position = 0;
        await using var gzip = new BgzfStream(ms, CompressionMode.Decompress);
        var readBuffer = new byte[data.Length];
        var read = await gzip.ReadAsync(readBuffer.AsMemory(0, data.Length));

        Assert.Equal(data, readBuffer);
        Assert.Equal(data.Length, read);
    }
}