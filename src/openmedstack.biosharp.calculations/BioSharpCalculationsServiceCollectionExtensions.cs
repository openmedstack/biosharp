namespace OpenMedStack.BioSharp.Calculations;

using System;
using Alignment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;

/// <summary>
/// Extension methods for registering BioSharp Calculations services with
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class BioSharpCalculationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers BioSharp calculation services:
    /// <list type="bullet">
    ///   <item><see cref="VariantCallingPipeline"/> factory delegate.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddBioSharpCalculations(this IServiceCollection services)
    {
        // Register a factory delegate so callers can create a pipeline with a reference sequence
        services.TryAddTransient<Func<Sequence, string, VariantCallingPipeline>>(
            _ => (reference, chromosome) => new VariantCallingPipeline(reference, chromosome));

        return services;
    }
}
