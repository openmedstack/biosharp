namespace OpenMedStack.BioSharp.Io;

using FastA;
using FastQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Extension methods for registering BioSharp I/O services with
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class BioSharpIoServiceCollectionExtensions
{
    /// <summary>
    /// Registers all BioSharp I/O readers and writers as transient services.
    /// </summary>
    public static IServiceCollection AddBioSharpIo(this IServiceCollection services)
    {
        services.TryAddTransient<FastQReader>(sp =>
        {
            var factory = sp.GetService<ILoggerFactory>();
            ILogger logger = factory != null
                ? factory.CreateLogger<FastQReader>()
                : NullLogger.Instance;
            return new FastQReader(logger);
        });
        services.TryAddTransient<FastAReader>();
        services.TryAddTransient<GffReader>();
        services.TryAddTransient<BedReader>();

        return services;
    }
}


