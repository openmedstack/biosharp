using System;
using Microsoft.Extensions.DependencyInjection;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddBioSharpIo_RegistersRequiredServices()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBioSharpIo();

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<Io.FastQ.FastQReader>());
        Assert.NotNull(provider.GetService<FastAReader>());
    }

    [Fact]
    public void AddBioSharpCalculations_RegistersPipelineFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBioSharpCalculations();

        var provider = services.BuildServiceProvider();

        var factory = provider.GetService<Func<Sequence, string, VariantCallingPipeline>>();
        Assert.NotNull(factory);
    }
}