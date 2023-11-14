namespace OpenMedStack.BioSharp.Io.Tests.FastQ;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Io.FastQ;
using Xunit;

public class IndexReaderWriterTests
{
    private readonly MemoryStream _stream = new();
    private readonly IndexReaderWriter _sut;

    public IndexReaderWriterTests()
    {
        _sut = new IndexReaderWriter(_stream);
    }

    [Fact]
    public async Task CanRoundTripIndexEntries()
    {
        var indexEntries = new[]
        {
            ("1", new BlockOffsetRecord(0, 375)),
            ("2", new BlockOffsetRecord(0, 523)),
            ("3", new BlockOffsetRecord(0, 867))
        };
        foreach (var (key, blockOffsetRecord) in indexEntries)
        {
            await _sut.Write(key, blockOffsetRecord);
        }
        await _stream.FlushAsync();
        _stream.Position = 0;
            
        var entries = await _sut.Read().ToListAsync();

        Assert.Equal(indexEntries, entries);
    }
}
