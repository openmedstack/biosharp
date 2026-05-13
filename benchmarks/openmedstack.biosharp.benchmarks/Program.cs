using BenchmarkDotNet.Running;
using OpenMedStack.BioSharp.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(PipelineBenchmarks).Assembly).Run(args: args);
