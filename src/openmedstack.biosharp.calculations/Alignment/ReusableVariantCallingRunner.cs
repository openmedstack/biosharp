namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Model;

/// <summary>
/// Reuses immutable reference alignment state while creating a fresh mutable pipeline per sample.
/// </summary>
public sealed class ReusableVariantCallingRunner
{
    private readonly string _chromosome;
    private readonly VariantCallingPipeline.PipelineOptions _options;

    public ReusableVariantCallingRunner(
        Sequence reference,
        string chromosome,
        VariantCallingPipeline.PipelineOptions? options = null)
        : this(ReferenceAlignmentContext.GetShared(reference), chromosome, options)
    {
    }

    public ReusableVariantCallingRunner(
        ReferenceAlignmentContext referenceContext,
        string chromosome,
        VariantCallingPipeline.PipelineOptions? options = null)
    {
        ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        _chromosome = chromosome ?? throw new ArgumentNullException(nameof(chromosome));
        _options = options ?? new VariantCallingPipeline.PipelineOptions();
    }

    public ReferenceAlignmentContext ReferenceContext { get; }

    public VariantCallingPipeline CreatePipeline()
    {
        return new VariantCallingPipeline(ReferenceContext, _chromosome, _options);
    }

    public void WarmReferenceIndex()
    {
        ReferenceContext.GetOrCreateIndex(CreateReferenceIndexOptions());
    }

    public async Task<VariantCallingPipeline.PipelineResult> RunPairedFastQ(
        IAsyncEnumerable<(Sequence R1, Sequence R2)> readPairs,
        CancellationToken cancellationToken = default)
    {
        var pipeline = CreatePipeline();
        await pipeline.LoadPairedFastQ(readPairs, cancellationToken).ConfigureAwait(false);
        return pipeline.BuildResult();
    }

    public async Task<VariantCallingPipeline.PipelineResult> RunFastQ(
        IAsyncEnumerable<Sequence> reads,
        CancellationToken cancellationToken = default)
    {
        var pipeline = CreatePipeline();
        await pipeline.LoadFastQ(reads, cancellationToken: cancellationToken).ConfigureAwait(false);
        return pipeline.BuildResult();
    }

    private ReferenceIndex.IndexOptions CreateReferenceIndexOptions()
    {
        return new ReferenceIndex.IndexOptions
        {
            SeedSize = _options.SeedSize,
            WindowPadding = _options.CandidateWindowPadding,
            MaxCandidateWindowsPerRead = _options.MaxCandidateWindowsPerRead,
            MaxSeedHitsPerKmer = _options.MaxSeedHitsPerKmer
        };
    }
}
