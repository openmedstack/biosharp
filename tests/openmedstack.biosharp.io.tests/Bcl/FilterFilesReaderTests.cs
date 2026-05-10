using System;

namespace OpenMedStack.BioSharp.Io.Tests.Bcl;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Io.Bcl;
using Xunit;

public class FilterFilesReaderTests : IAsyncLifetime
{
    private FilterFileReader _reader = null!;

    [Fact]
    public void CanRead()
    {
        var amount = _reader.Count();

        Assert.Equal(_reader.NumClusters, amount);
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var dataDir = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Directory;
        if (dataDir == null)
        {
            Assert.Skip("Cannot locate executing assembly directory");
        }

        var root = dataDir.Parent?.Parent?.Parent?.Parent;
        if (root == null)
        {
            Assert.Skip("Cannot navigate to data directory");
        }

        var filterPath = Path.Combine(root.FullName, "data/sampledata/Data/Intensities/BaseCalls/L001/s_1_1101.filter");

        if (!File.Exists(filterPath))
        {
            Assert.Skip($"No .filter test file available at: {filterPath}");
        }

        var filterFileInfo = new FileInfo(filterPath);
        _reader = await FilterFileReader
                .Create(filterFileInfo)
            ;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
