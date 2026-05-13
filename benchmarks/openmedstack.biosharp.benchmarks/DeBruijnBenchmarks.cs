namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using OpenMedStack.BioSharp.Model;

[MemoryDiagnoser]
[ShortRunJob]
public class DeBruijnBenchmarks
{
    private Sequence[] _reads = null!;

    [Params(15, 31)] public int KmerSize { get; set; }

    [Params(2048, 8192)] public int ReadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var reference = string.Concat(Enumerable.Repeat("ACGTGATTACAGGTTCCGATTA", 16));
        _reads = Enumerable.Range(0, ReadCount)
            .Select(index =>
            {
                var start = index % (reference.Length - 96);
                var bases = reference.Substring(start, 96).ToCharArray();
                if (index % 8 == 0)
                {
                    bases[32] = bases[32] == 'A' ? 'T' : 'A';
                }

                if (index % 16 == 0)
                {
                    bases[48] = bases[48] == 'C' ? 'G' : 'C';
                }

                var sequence = new string(bases);
                return new Sequence(
                    $"dbg-{index}",
                    sequence.AsMemory(),
                    new string('I', sequence.Length).AsMemory());
            })
            .ToArray();
    }

    [Benchmark]
    public async Task<int> BuildGraphAndCountNodes()
    {
        var graph = new DeBruijnGraph(KmerSize, System.Linq.AsyncEnumerable.ToAsyncEnumerable(_reads));
        var nodes = await graph.GetNodes(CancellationToken.None).ConfigureAwait(false);
        return nodes.Count;
    }

    [Benchmark]
    public async Task<int> BuildGraphAndAssemble()
    {
        var graph = new DeBruijnGraph(KmerSize, System.Linq.AsyncEnumerable.ToAsyncEnumerable(_reads));
        var assemblies = 0;
        await foreach (var _ in graph.Assemble(CancellationToken.None).ConfigureAwait(false))
        {
            assemblies++;
        }

        return assemblies;
    }
}
