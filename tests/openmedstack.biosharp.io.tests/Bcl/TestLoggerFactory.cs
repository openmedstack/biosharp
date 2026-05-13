using Microsoft.Extensions.Logging;

namespace OpenMedStack.BioSharp.Io.Tests.Bcl;

internal class TestLoggerFactory : ILoggerFactory
{
    private readonly ILoggerProvider _provider;

    public TestLoggerFactory(ILoggerProvider provider)
    {
        _provider = provider;
    }

    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _provider.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }
}