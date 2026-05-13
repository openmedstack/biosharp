namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Model;

/// <summary>
/// Holds reusable, immutable reference state that can be shared across multiple
/// variant-calling runs against the same reference.
/// </summary>
public sealed class ReferenceAlignmentContext
{
    private static readonly ConditionalWeakTable<Sequence, ReferenceAlignmentContext> SharedContexts = new();
    private readonly ConcurrentDictionary<ReferenceIndex.OptionsKey, Lazy<ReferenceIndex>> _indexes = new();

    public ReferenceAlignmentContext(Sequence reference)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        ReferenceFingerprint = ReferenceIndex.ComputeReferenceFingerprint(reference);
    }

    public Sequence Reference { get; }

    public ulong ReferenceFingerprint { get; }

    public static ReferenceAlignmentContext GetShared(Sequence reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return SharedContexts.GetValue(reference, static sequence => new ReferenceAlignmentContext(sequence));
    }

    public ReferenceIndex GetOrCreateIndex(ReferenceIndex.IndexOptions? options = null)
    {
        var normalizedOptions = ReferenceIndex.CloneOptions(options);
        var key = ReferenceIndex.CreateKey(normalizedOptions);
        var lazyIndex = _indexes.GetOrAdd(
            key,
            _ => new Lazy<ReferenceIndex>(
                () => new ReferenceIndex(Reference, normalizedOptions),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazyIndex.Value;
    }

    public ReferenceIndex LoadIndex(string path, ReferenceIndex.IndexOptions? expectedOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        return LoadIndex(stream, expectedOptions);
    }

    public ReferenceIndex LoadIndex(Stream stream, ReferenceIndex.IndexOptions? expectedOptions = null)
    {
        var index = ReferenceIndex.Load(Reference, stream, expectedOptions, ReferenceFingerprint);
        var lazyIndex = _indexes.GetOrAdd(
            index.Key,
            _ => new Lazy<ReferenceIndex>(() => index, LazyThreadSafetyMode.ExecutionAndPublication));
        return lazyIndex.Value;
    }

    public void SaveIndex(string path, ReferenceIndex.IndexOptions? options = null)
    {
        GetOrCreateIndex(options).Save(path);
    }
}
